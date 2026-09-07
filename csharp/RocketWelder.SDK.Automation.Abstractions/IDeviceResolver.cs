using RocketWelder.SDK.Abstractions;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// Resolves LIVE device instances. The counterpart of <see cref="IDeviceQuery"/>, which deliberately
/// returns configuration snapshots only. A plugin whose device composes on another device (a welder on
/// a robot's connection, an axis on a controller) uses this and nothing else.
///
/// <para><b>Never resolve in a device factory.</b> Devices are constructed one at a time in event order
/// — the order the operator added them — so the device you need may not exist yet. Resolve lazily and
/// re-resolve on <see cref="DeviceInstanceChanged"/>.</para>
/// </summary>
public interface IDeviceResolver
{
    /// <summary>The live instance registered for <paramref name="id"/>, or null when the device does not
    /// exist, has not been built, or is not assignable to <typeparamref name="T"/>.</summary>
    T? Get<T>(DeviceId id) where T : class;

    /// <summary>Every distinct live instance assignable to <typeparamref name="T"/>, in no defined
    /// order. Each instance appears once however many keys it is registered under.</summary>
    IEnumerable<T> GetAll<T>() where T : class;

    /// <summary>Raised after a device instance has been created, replaced (rename / reconfigure) or torn
    /// down. Any reference a consumer holds for that <see cref="DeviceId"/> is stale. Raised on the
    /// thread that performed the change; handlers must be short and must not throw.</summary>
    event Action<DeviceId>? DeviceInstanceChanged;
}
