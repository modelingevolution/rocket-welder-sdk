namespace RocketWelder.SDK.Abstractions;

/// <summary>
/// Optional capability: a device whose latched faults can be cleared from the host.
/// </summary>
/// <remarks>
/// <para>
/// Implemented BESIDE the device's kind interface, never added to it (see <see cref="IFaultReporting"/>).
/// The host's "Reset errors" action resets every <see cref="IFaultResettable"/> in the registry alongside the
/// resets its kind interfaces already carry (<c>IRobot.ResetAllErrors</c>, <c>IMotionAxis.ResetAsync</c>), and
/// reports one row per device.
/// </para>
/// <para>
/// A reset clears latches and never moves anything. It returns when the device has acknowledged the clear —
/// or when the adapter has given up waiting for it, in which case <see cref="IFaultReporting.Fault"/>, if
/// implemented, still says what stands. It should honour <paramref name="ct"/>: the host gives each device a
/// bounded budget so one stuck device cannot hold the whole station's reset.
/// </para>
/// </remarks>
public interface IFaultResettable
{
    /// <summary>Clears the device's latched faults.</summary>
    ValueTask ResetFaultsAsync(CancellationToken ct = default);
}
