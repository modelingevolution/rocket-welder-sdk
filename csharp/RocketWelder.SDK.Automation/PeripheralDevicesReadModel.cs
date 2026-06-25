using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using MicroPlumberd;
using Microsoft.Extensions.Logging;
using RocketWelder.SDK.Abstractions;
using RocketWelder.SDK.Automation;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// Read model for all registered peripheral devices.
///
/// <para>
/// <b><c>[OutputStream("PeripheralDevicesReadModel_v1")]</c></b> — FROZEN stream name. Changing it
/// breaks any catch-up subscription keyed on this name.
/// </para>
///
/// <para>
/// After each device rebuild (Created / Configured / Renamed), the read model invokes
/// <see cref="DeviceTypeInfo.GetSignals"/> on the live device instance and calls
/// <see cref="IDeviceSignalPublisher.Publish"/>. On removal or rebuild, it calls
/// <see cref="IDeviceSignalPublisher.Release"/> to dispose the prior signal tokens.
/// </para>
///
/// <para>
/// <b>No rw2-specific concerns.</b> The SDK read model owns state only — it does NOT reference
/// <c>ProgramService</c>, <c>PeripheralDeviceAvailabilityMonitor</c>, or any host-specific
/// service. Hosts observe <see cref="Items"/> for UI binding and query
/// <see cref="GetById"/>/<see cref="GetByInterface"/> to access device instances.
/// </para>
/// </summary>
[EventHandler]
[OutputStream("PeripheralDevicesReadModel_v1")]
public partial class PeripheralDevicesReadModel : ICaughtUpHandler
{
    private readonly DeviceTypeRegistry _deviceTypeRegistry;
    private readonly IDeviceSignalPublisher _signalPublisher;
    private readonly ILogger<PeripheralDevicesReadModel> _logger;
    private readonly ConcurrentDictionary<DeviceId, PeripheralDeviceItem> _byId = new();

    /// <summary>
    /// All live peripheral devices. Hosts can bind to this collection (BCL
    /// <see cref="ObservableCollection{T}"/>) for change notification.
    /// </summary>
    public ObservableCollection<PeripheralDeviceItem> Items { get; } = [];

    /// <summary><see langword="true"/> after the catch-up subscription has processed all stored events.</summary>
    public bool IsLive { get; private set; }

    public PeripheralDevicesReadModel(
        DeviceTypeRegistry deviceTypeRegistry,
        IDeviceSignalPublisher signalPublisher,
        ILogger<PeripheralDevicesReadModel> logger)
    {
        _deviceTypeRegistry = deviceTypeRegistry;
        _signalPublisher = signalPublisher;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task CaughtUp()
    {
        IsLive = true;
        _logger.LogInformation("PeripheralDevicesReadModel caught up with {Count} devices.", _byId.Count);
        return Task.CompletedTask;
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private async Task Given(Metadata m, PeripheralDeviceCreated ev)
    {
        var deviceId = m.StreamId<DeviceId>();
        var item = new PeripheralDeviceItem
        {
            Id = deviceId,
            DeviceType = ev.DeviceType,
            InterfaceType = ev.InterfaceType,
            Name = ev.Name,
            Number = ev.Number
        };

        _byId[deviceId] = item;
        await RebuildAsync(item);
        Items.Add(item);
    }

    private async Task Given(Metadata m, PeripheralDeviceRenamed ev)
    {
        var deviceId = m.StreamId<DeviceId>();
        if (!_byId.TryGetValue(deviceId, out var item)) return;

        var oldName = item.Name;
        item.Name = ev.Name;

        if (IsLive)
            _logger.LogInformation("Peripheral device renamed: {Old} → {New}", oldName, ev.Name);
        else
            _logger.LogDebug("Peripheral device renamed: {Old} → {New} [replay]", oldName, ev.Name);

        await RebuildAsync(item);
    }

    private async Task Given(Metadata m, PeripheralDeviceConfigured ev)
    {
        var deviceId = m.StreamId<DeviceId>();
        if (!_byId.TryGetValue(deviceId, out var item)) return;

        item.Config = item.Config.Apply(ev.Set, ev.Unset);
        await RebuildAsync(item);
    }

    private Task Given(Metadata m, PeripheralDeviceRemoved ev)
    {
        var deviceId = m.StreamId<DeviceId>();
        if (!_byId.TryRemove(deviceId, out var item)) return Task.CompletedTask;

        Items.Remove(item);
        _signalPublisher.Release(deviceId);
        DisposeInstance(item);

        if (IsLive)
            _logger.LogInformation("Peripheral device removed: {Name} ({DeviceType})", item.Name, item.DeviceType);
        else
            _logger.LogDebug("Peripheral device removed: {Name} ({DeviceType}) [replay]", item.Name, item.DeviceType);

        return Task.CompletedTask;
    }

    // ── Query methods (used by command handler) ───────────────────────────────

    /// <summary>Returns the <see cref="PeripheralDeviceItem"/> for the given id, or <see langword="null"/>.</summary>
    public PeripheralDeviceItem? GetById(DeviceId id)
        => _byId.TryGetValue(id, out var item) ? item : null;

    /// <summary>Returns all live devices for the given <paramref name="interfaceType"/>.</summary>
    public IEnumerable<PeripheralDeviceItem> GetByInterface(string interfaceType)
        => _byId.Values.Where(x => x.InterfaceType == interfaceType);

    /// <summary>
    /// Returns the next available ordinal number for the given <paramref name="interfaceType"/>
    /// (fills gaps left by removed devices).
    /// </summary>
    public int GetNextNumber(string interfaceType)
    {
        var existing = new HashSet<int>(
            _byId.Values
                .Where(x => x.InterfaceType == interfaceType)
                .Select(x => x.Number));

        var next = 1;
        while (existing.Contains(next)) next++;
        return next;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task RebuildAsync(PeripheralDeviceItem item)
    {
        var typeInfo = _deviceTypeRegistry.Get(item.DeviceType);
        if (typeInfo is null)
        {
            _logger.LogWarning("Unknown device type '{DeviceType}' — skipping rebuild for {Name}", item.DeviceType, item.Name);
            return;
        }

        // Release prior signal registrations and dispose the old instance
        _signalPublisher.Release(item.Id);
        DisposeInstance(item);

        try
        {
            var instance = typeInfo.Factory(item.Config, item.Id);
            item.Instance = instance;
            item.Status = PeripheralDeviceStatus.Disconnected;

            if (typeInfo.GetSignals is { } getSignals)
            {
                var signals = getSignals(instance);
                _signalPublisher.Publish(item.Id, signals);
            }

            if (IsLive)
                _logger.LogInformation("Registered peripheral device {Name} ({DeviceType})", item.Name, item.DeviceType);
            else
                _logger.LogDebug("Registered peripheral device {Name} ({DeviceType}) [replay]", item.Name, item.DeviceType);
        }
        catch (Exception ex)
        {
            item.Instance = null;
            item.Status = PeripheralDeviceStatus.Error;
            item.StatusMessage = ex.Message;
            _logger.LogError(ex, "Failed to create peripheral device {Name} ({DeviceType})", item.Name, item.DeviceType);
        }

        await Task.CompletedTask; // keep async signature for future I/O
    }

    private static void DisposeInstance(PeripheralDeviceItem item)
    {
        switch (item.Instance)
        {
            case IAsyncDisposable ad:
                try { _ = ad.DisposeAsync(); } catch { /* best effort */ }
                break;
            case IDisposable d:
                try { d.Dispose(); } catch { /* best effort */ }
                break;
        }
        item.Instance = null;
    }
}
