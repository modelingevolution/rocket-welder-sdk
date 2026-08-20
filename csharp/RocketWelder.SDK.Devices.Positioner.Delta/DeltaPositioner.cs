using Microsoft.Extensions.Logging;
using RocketWelder.SDK.Abstractions;

namespace RocketWelder.SDK.Devices.Positioner.Delta;

/// <summary>
/// A welding positioner built from Delta VFD-C2000 drives, one per axis, each on its own Modbus TCP
/// endpoint. Axes are mechanically independent and can move at the same time.
/// </summary>
public sealed class DeltaPositioner : IPositioner
{
    private readonly Dictionary<string, DeltaAxis> _byName;
    private readonly List<ModbusChannel> _channels = [];
    private readonly ILogger<DeltaPositioner>? _logger;
    private bool _connected;
    private bool _disposed;

    /// <summary>Builds a positioner from per-axis configuration.</summary>
    /// <param name="id">Device identity assigned by the host.</param>
    /// <param name="axes">Axis configurations; at least one.</param>
    /// <param name="store">Persistence for the captured zero. Defaults to in-memory, which forces
    /// a re-home after every restart.</param>
    /// <param name="logger">Optional logger.</param>
    public DeltaPositioner(DeviceId id, IReadOnlyList<DeltaAxisConfig> axes,
        IAxisStateStore? store = null, ILogger<DeltaPositioner>? logger = null)
    {
        if (axes.Count == 0) throw new ArgumentException("A positioner needs at least one axis", nameof(axes));

        Id = id;
        _logger = logger;
        store ??= new InMemoryAxisStateStore();

        var built = new List<DeltaAxis>(axes.Count);
        foreach (var cfg in axes)
        {
            var channel = new ModbusChannel(cfg.Host, cfg.Port, logger);
            _channels.Add(channel);
            built.Add(new DeltaAxis(cfg, channel, store, logger));
        }

        Axes = built;
        _byName = built.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);
        Address = new Uri($"modbus://{axes[0].Host}");
    }

    /// <inheritdoc/>
    public DeviceId Id { get; }

    /// <inheritdoc/>
    public Uri Address { get; set; }

    /// <inheritdoc/>
    public bool IsConnected => _connected && _channels.All(c => c.IsConnected);

    /// <inheritdoc/>
    public IReadOnlyList<IPositionerAxis> Axes { get; }

    /// <inheritdoc/>
    public IPositionerAxis this[string name] => _byName.TryGetValue(name, out var axis)
        ? axis
        : throw new KeyNotFoundException(
            $"No axis '{name}'. Available: {string.Join(", ", _byName.Keys)}");

    /// <inheritdoc/>
    public bool TryGetAxis(string name, out IPositionerAxis axis)
    {
        if (_byName.TryGetValue(name, out var found))
        {
            axis = found;
            return true;
        }

        axis = null!;
        return false;
    }

    /// <inheritdoc/>
    public bool IsReady => Axes.All(a => a.Status.Ready == true);

    /// <inheritdoc/>
    public event EventHandler? Connected;

    /// <inheritdoc/>
    public event EventHandler? Disconnected;

    /// <inheritdoc/>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        var probes = _channels.Select(c => c.IsAvailableAsync(TimeSpan.FromSeconds(2), ct));
        return (await Task.WhenAll(probes)).All(ok => ok);
    }

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        foreach (var channel in _channels)
            await channel.ConnectAsync(ct);

        foreach (var axis in Axes.Cast<DeltaAxis>())
            await axis.InitialiseAsync(ct);

        _connected = true;
        Connected?.Invoke(this, EventArgs.Empty);
        _logger?.LogInformation("Positioner {Id} connected: {Axes}", Id,
            string.Join(", ", Axes.Select(a => $"{a.Name}@{((DeltaAxis)a).Config.Host}")));
    }

    /// <inheritdoc/>
    public void Disconnect()
    {
        foreach (var channel in _channels) channel.Disconnect();
        if (!_connected) return;
        _connected = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public async Task HomeAllAsync(CancellationToken ct = default)
    {
        var homing = Axes
            .Where(a => a.RequiresHoming)
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
    public async Task<IReadOnlyList<PositionerAxisStatus>> ReadAllStatusAsync(CancellationToken ct = default)
    {
        var results = new List<PositionerAxisStatus>(Axes.Count);
        foreach (var axis in Axes)
            results.Add(await axis.ReadStatusAsync(ct));
        return results;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var axis in Axes.Cast<DeltaAxis>()) axis.Dispose();
        foreach (var channel in _channels) channel.Dispose();
    }
}
