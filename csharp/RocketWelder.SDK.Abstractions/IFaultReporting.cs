namespace RocketWelder.SDK.Abstractions;

/// <summary>
/// Optional capability: a device that can say what is wrong with it.
/// </summary>
/// <remarks>
/// <para>
/// Implemented BESIDE the device's kind interface (<c>IWeldingMachine</c>, <c>IRobot</c>, …), never added to
/// it — the host tests the instance (<c>device is IFaultReporting</c>), so a plugin built against an older SDK
/// keeps working and simply reports no fault. This is the same capability-by-interface rule the host's mobile
/// surface registry and station STOP roster already apply.
/// </para>
/// <para>
/// <see cref="Fault"/> MUST be a cheap read of a value the adapter's own poll maintains — never an on-demand
/// probe. The host reads it on every status tick and on the render path of the Devices page.
/// </para>
/// </remarks>
public interface IFaultReporting
{
    /// <summary>The fault the device is reporting now, or null when it reports none (or none is known yet).</summary>
    DeviceFault? Fault { get; }
}
