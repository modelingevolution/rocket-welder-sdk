using ModelingEvolution.Signals;
using RocketWelder.SDK.Abstractions;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// Port for publishing device signals to the host's signal catalog.
///
/// <para>
/// Lives in the engine assembly (<c>RocketWelder.SDK.Automation</c>) and is inverted so the engine
/// does NOT depend on the signal-processing repo or <c>IMutableSignalCatalog</c>. The host (rw2 or
/// the App) supplies the adapter that bridges to its catalog.
/// </para>
///
/// <para>
/// <see cref="Publish"/> is called by <see cref="PeripheralDevicesReadModel"/> after each device
/// rebuild (Created / Configured). <see cref="Release"/> is called before each rebuild and on
/// device removal, so existing signal tokens are disposed before new ones are registered.
/// </para>
///
/// <para>Token lifetime = device lifetime. The no-op default is registered by
/// <c>AddPeripheralDevices()</c> and can be overridden by the host.</para>
/// </summary>
public interface IDeviceSignalPublisher
{
    /// <summary>
    /// Publishes the <paramref name="signals"/> enumeration for the given <paramref name="deviceId"/>.
    /// Replaces any prior registration for the same device (callers ensure <see cref="Release"/>
    /// was called first).
    /// </summary>
    void Publish(DeviceId deviceId, IEnumerable<ISignal<float>> signals);

    /// <summary>
    /// Releases all signal registrations for the given <paramref name="deviceId"/>.
    /// Safe to call even if no signals were published for that device.
    /// </summary>
    void Release(DeviceId deviceId);
}
