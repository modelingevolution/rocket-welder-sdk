using RocketWelder.SDK.Abstractions;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// Lean, MicroPlumberd-free contract DTO describing one registered device — a flat immutable snapshot
/// exposed across the <see cref="IDeviceQuery"/> port, deliberately WITHOUT any
/// <c>ObservableCollection</c> / <c>INotifyPropertyChanged</c> / live device instance. The host read
/// model projects its rich item onto this for consumers that only need to query device facts.
///
/// <para>
/// Named <c>DeviceSnapshot</c> (not <c>DeviceInfo</c>) to avoid colliding with the established
/// <c>RocketWelder.SDK.Http.Devices.DeviceInfo</c> in hosts that import both globally.
/// </para>
/// </summary>
/// <param name="Id">Stable device identity (minted at creation, never changes).</param>
/// <param name="DeviceType">CLR type discriminator (e.g. <c>"FroniusWeldingMachine_TPS5000"</c>).</param>
/// <param name="InterfaceType">Interface-role string (e.g. <c>"IWeldingMachine"</c>).</param>
/// <param name="Name">Operator-assigned name (unique within the interface type).</param>
/// <param name="Number">1-based ordinal within the interface type (gap-fills on removal).</param>
/// <param name="Status">Current device health as observed by the host read model.</param>
/// <param name="Config">Current configuration snapshot.</param>
public record DeviceSnapshot(
    DeviceId Id,
    string DeviceType,
    string InterfaceType,
    string Name,
    int Number,
    PeripheralDeviceStatus Status,
    ConfigSet Config);

/// <summary>
/// MicroPlumberd-free read port over the host's device read model. Consumers that only need to
/// <em>query</em> device facts (resolution, ordinal allocation, name-uniqueness checks, config
/// snapshots) depend on this instead of the host read model's rich, host-only surface
/// (<c>ObservableCollection</c> of live items, catch-up lifecycle, signal publication).
///
/// <para>
/// Epic 052 cutover: this replaces the obsolete host-side <c>IPeripheralDevicesReadModel</c> name
/// as the cross-assembly query seam ("Peripheral" is dropped). It lives in
/// <c>RocketWelder.SDK.Automation.Abstractions</c> and carries no event-sourcing dependency, so the
/// App host (or any non-rw2 consumer) can bind it without referencing MicroPlumberd.
/// </para>
/// </summary>
public interface IDeviceQuery
{
    /// <summary>Raised after a device's configuration changes (live or replay), keyed by device id.</summary>
    event Action<DeviceId>? DeviceConfigChanged;

    /// <summary>Returns the snapshot for <paramref name="id"/>, or <see langword="null"/> if unknown.</summary>
    DeviceSnapshot? GetById(DeviceId id);

    /// <summary>Returns snapshots of all live devices for the given <paramref name="interfaceType"/>.</summary>
    IEnumerable<DeviceSnapshot> GetByInterface(string interfaceType);

    /// <summary>Returns the next free 1-based ordinal for <paramref name="interfaceType"/> (fills gaps).</summary>
    int GetNextNumber(string interfaceType);
}
