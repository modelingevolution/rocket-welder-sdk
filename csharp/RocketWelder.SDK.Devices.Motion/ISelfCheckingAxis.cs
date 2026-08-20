namespace RocketWelder.SDK.Devices.Motion;

/// <summary>
/// Optional capability — commissioning diagnostics (FR-7). An axis that can verify its own sign
/// convention implements this <b>in addition to</b> its typed leaf.
///
/// <para>
/// <b>Deliberately NOT on <see cref="IMotionAxis"/>.</b> It is not a motion verb, so keeping it off
/// the base is what keeps FR-12's block palette closed — the builder offers
/// <c>MoveAbsolute</c> / <c>MoveRelative</c> / <c>MoveVelocity</c> / <c>Home</c> / <c>Stop</c> and
/// nothing else, because those are exactly the base's own verbs. And not every axis supports it, so
/// putting it on the base would force every implementation to declare a method most of them can
/// only throw from — the "truthful no" this contract is built to avoid (FR-4).
/// </para>
///
/// <para>
/// A caller asks by type: <c>if (axis is ISelfCheckingAxis c) await c.VerifyDirectionAsync(ct);</c>
/// </para>
/// </summary>
public interface ISelfCheckingAxis
{
    /// <summary>
    /// Jogs a short distance and confirms the sign convention, refusing to run from a tripped limit.
    ///
    /// <para>
    /// This exists because an inverted axis drives the correct distance the WRONG way and reads as
    /// a broken control loop rather than a wiring fault — a failure mode that cost a full
    /// diagnostic session on the bench and that this check finds in about three seconds.
    /// </para>
    /// </summary>
    /// <param name="ct">Cancellation; cancelling stops the axis.</param>
    /// <exception cref="MotionException">A travel limit is active
    /// (<see cref="MotionError.LimitTripped"/>), the axis is not in
    /// <see cref="AxisState.Standstill"/> (<see cref="MotionError.Busy"/>), or the observed
    /// direction contradicts the commanded one.</exception>
    Task VerifyDirectionAsync(CancellationToken ct = default);
}
