namespace RocketWelder.SDK.Automation;

/// <summary>
/// Health status of a registered device as observed by the host read model.
///
/// <para>
/// Epic 052 cutover: extracted out of the (now host-owned) device item into the MicroPlumberd-free
/// <c>RocketWelder.SDK.Automation.Abstractions</c> so both the host read model and the lean
/// <see cref="IDeviceQuery"/> port can reference it without pulling in any event-sourcing dependency.
/// The namespace stays <c>RocketWelder.SDK.Automation</c> so existing consumers need no edits.
/// </para>
/// </summary>
public enum PeripheralDeviceStatus
{
    /// <summary>No live connection to the device (initial state, or after a clean teardown).</summary>
    Disconnected,

    /// <summary>A connection attempt is in progress.</summary>
    Connecting,

    /// <summary>The device is connected and healthy.</summary>
    Connected,

    /// <summary>The device instance failed to build or reported a fault (see status message).</summary>
    Error
}
