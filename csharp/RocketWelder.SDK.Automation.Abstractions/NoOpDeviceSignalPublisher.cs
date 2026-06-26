using ModelingEvolution.Signals;
using RocketWelder.SDK.Abstractions;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// No-op implementation of <see cref="IDeviceSignalPublisher"/>. Registered by default in
/// <c>AddPeripheralDevices()</c>. Hosts that need real signal publication replace this with
/// an adapter over their <c>IMutableSignalCatalog</c>.
/// </summary>
public sealed class NoOpDeviceSignalPublisher : IDeviceSignalPublisher
{
    /// <inheritdoc/>
    public void Publish(DeviceId deviceId, IEnumerable<ISignal<float>> signals) { }

    /// <inheritdoc/>
    public void Release(DeviceId deviceId) { }
}
