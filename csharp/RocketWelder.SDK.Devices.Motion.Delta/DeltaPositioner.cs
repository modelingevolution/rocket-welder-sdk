using Microsoft.Extensions.Logging;
using RocketWelder.SDK.Abstractions;

namespace RocketWelder.SDK.Devices.Motion.Delta;

/// <summary>
/// A welding positioner built from Delta VFD-C2000 drives, one per axis, each on its own Modbus TCP
/// endpoint. Axes are mechanically independent and can move at the same time.
///
/// <para>
/// The marker interface <see cref="IPositioner"/> is the device's classification (FR-3): a program
/// resolves this device with <c>ctx.GetRequiredDevice&lt;IPositioner&gt;()</c> and never by the
/// vendor discriminator, so swapping vendors while redeclaring the same axis names does not break a
/// stored program (D-e).
/// </para>
///
/// <para>
/// <b>FR-11 lives at this level.</b> Each drive gets its own <see cref="DeltaHeartbeat"/>, started by
/// <see cref="ConnectAsync"/> and stopped deliberately on disconnect — connection-lifetime, not
/// motion-lifetime. Attaching means taking the advisory lease first: an instance that sees a live
/// foreign heartbeat refuses, names the owner it saw, and retries at 1 Hz until expiry (AC-12).
/// </para>
/// </summary>
public sealed class DeltaPositioner : IPositioner
{
    private readonly Dictionary<string, DeltaAxis> _byName;
    private readonly List<IModbusChannel> _channels = [];
    private readonly List<DeltaHeartbeat> _heartbeats = [];
    private readonly ILogger<DeltaPositioner>? _logger;
    private readonly TimeSpan? _leaseTimeout;
    private bool _connected;
    private bool _disposed;

    /// <summary>Builds a positioner from per-axis configuration.</summary>
    /// <param name="id">Device identity assigned by the host.</param>
    /// <param name="axes">Axis configurations; at least one.</param>
    /// <param name="ownerId">This station's unique 16-bit id for the FR-11 advisory lease. Must be
    /// non-zero: 0 is the unowned marker in D131.</param>
    /// <param name="store">Persistence for the captured zero. Defaults to in-memory, which forces a
    /// re-home after every restart.</param>
    /// <param name="leaseTimeout">How long <see cref="ConnectAsync"/> keeps retrying a refused lease
    /// before giving up, or <see langword="null"/> to retry until the caller's token fires.</param>
    /// <param name="logger">Optional logger.</param>
    public DeltaPositioner(DeviceId id, IReadOnlyList<DeltaAxisConfig> axes, ushort ownerId,
        IAxisStateStore? store = null, TimeSpan? leaseTimeout = null,
        ILogger<DeltaPositioner>? logger = null)
        : this(id, axes, ownerId, store, leaseTimeout, logger,
            cfg => new ModbusChannel(cfg.Host, cfg.Port, logger))
    {
    }

    /// <summary>Test seam: builds the positioner over supplied channels instead of real sockets.</summary>
    internal DeltaPositioner(DeviceId id, IReadOnlyList<DeltaAxisConfig> axes, ushort ownerId,
        IAxisStateStore? store, TimeSpan? leaseTimeout, ILogger<DeltaPositioner>? logger,
        Func<DeltaAxisConfig, IModbusChannel> channelFactory)
    {
        ArgumentNullException.ThrowIfNull(axes);
        if (axes.Count == 0) throw new ArgumentException("A positioner needs at least one axis", nameof(axes));
        if (ownerId == AdvisoryLease.Unowned)
            throw new ArgumentOutOfRangeException(nameof(ownerId),
                "0 is the unowned marker in D131; give this station a non-zero unique id");

        Id = id;
        _logger = logger;
        _leaseTimeout = leaseTimeout;
        store ??= new InMemoryAxisStateStore();

        var built = new List<DeltaAxis>(axes.Count);
        foreach (var cfg in axes)
        {
            var channel = channelFactory(cfg);
            _channels.Add(channel);

            var axis = new DeltaAxis(cfg, channel, store, logger);
            built.Add(axis);

            var heartbeat = new DeltaHeartbeat(cfg.Name, channel, ownerId, cfg.HeartbeatInterval,
                cfg.WatchdogStallWindow, logger);
            heartbeat.Ticked += (_, _) => _ = axis.OnHeartbeatTickAsync(CancellationToken.None);
            heartbeat.WatchdogTripped += (_, _) => axis.OnWatchdogTripped();
            _heartbeats.Add(heartbeat);
        }

        Axes = built;
        _byName = built.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);
        Address = new Uri($"modbus://{axes[0].Host}");
        OwnerId = ownerId;
    }

    /// <inheritdoc/>
    public DeviceId Id { get; }

    /// <summary>The station-unique 16-bit id this instance writes into D131 (FR-11).</summary>
    public ushort OwnerId { get; }

    /// <summary>The first drive's endpoint, as the host's device address.</summary>
    public Uri Address { get; set; }

    /// <summary>Every channel is open.</summary>
    public bool IsConnected => _connected && _channels.All(c => c.IsConnected);

    /// <inheritdoc/>
    public IReadOnlyList<IMotionAxis> Axes { get; }

    /// <inheritdoc/>
    public IMotionAxis this[string name] => _byName.TryGetValue(name, out var axis)
        ? axis
        : throw new MotionException(MotionError.UnknownAxis,
            $"Device '{Id}' has no axis named '{name}'. Declared axes: {string.Join(", ", _byName.Keys)}",
            name);

    /// <summary>Every axis is powered and would accept a motion command.</summary>
    public bool IsReady => Axes.All(a => a.State == AxisState.Standstill);

    /// <summary>Raised once every drive is attached and beating.</summary>
    public event EventHandler? Connected;

    /// <summary>Raised on a deliberate disconnect.</summary>
    public event EventHandler? Disconnected;

    /// <summary>Probes every drive's endpoint without a Modbus transaction.</summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        var probes = _channels.Select(c => c.IsAvailableAsync(TimeSpan.FromSeconds(2), ct));
        return (await Task.WhenAll(probes)).All(ok => ok);
    }

    /// <summary>
    /// Opens every drive, <b>takes the advisory lease</b>, starts the heartbeat and applies the drive
    /// setup — in that order. The lease is taken before anything is written that could move the
    /// machine, which is the whole point of checking it.
    /// </summary>
    /// <exception cref="MotionException"><see cref="MotionError.LeaseHeld"/> — a live foreign
    /// heartbeat still holds a drive when <c>leaseTimeout</c> elapses; the message names the owner.</exception>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        for (var i = 0; i < _channels.Count; i++)
        {
            await _channels[i].ConnectAsync(ct);
            await _heartbeats[i].AcquireAsync(_leaseTimeout, ct);
            _heartbeats[i].Start();
        }

        foreach (var axis in Axes.Cast<DeltaAxis>())
            await axis.InitialiseAsync(ct);

        _connected = true;
        Connected?.Invoke(this, EventArgs.Empty);
        _logger?.LogInformation("Positioner {Id} connected as owner {Owner}: {Axes}", Id, OwnerId,
            string.Join(", ", Axes.Cast<DeltaAxis>().Select(a => $"{a.Name}@{a.Config.Host}")));
    }

    /// <summary>
    /// Stops beating, releases the lease and closes every channel. A process that is <i>killed</i>
    /// rather than disconnected cannot do this — which is exactly the case FR-11's drive-side
    /// watchdog exists for.
    /// </summary>
    public async Task DisconnectAsync()
    {
        foreach (var heartbeat in _heartbeats) await heartbeat.StopAsync();
        foreach (var channel in _channels) channel.Disconnect();

        if (!_connected) return;
        _connected = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc cref="DisconnectAsync"/>
    public void Disconnect() => DisconnectAsync().GetAwaiter().GetResult();

    /// <inheritdoc/>
    public async Task HomeAllAsync(CancellationToken ct = default)
    {
        var homing = Axes
            .Where(a => a.Capabilities.HasFlag(AxisCapabilities.Homing))
            .Select(a => a.HomeAsync(ct))
            .ToArray();
        if (homing.Length == 0) return;

        // Wait for every axis before surfacing a failure: leaving the others running while one
        // faults is how a positioner ends up in a state nobody commanded.
        try
        {
            await Task.WhenAll(homing);
        }
        catch
        {
            throw homing.First(t => t.IsFaulted).Exception!.InnerException!;
        }
    }

    /// <inheritdoc/>
    public Task StopAllAsync(CancellationToken ct = default) =>
        Task.WhenAll(Axes.Select(a => a.StopAsync(ct)));

    /// <summary>Reads a fresh status from every axis.</summary>
    public async Task<IReadOnlyList<AxisStatus>> ReadAllStatusAsync(CancellationToken ct = default)
    {
        var results = new List<AxisStatus>(Axes.Count);
        foreach (var axis in Axes)
            results.Add(await axis.ReadStatusAsync(ct));
        return results;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var heartbeat in _heartbeats) heartbeat.DisposeAsync().AsTask().GetAwaiter().GetResult();
        foreach (var axis in Axes.Cast<DeltaAxis>()) axis.Dispose();
        foreach (var channel in _channels) channel.Dispose();
    }
}
