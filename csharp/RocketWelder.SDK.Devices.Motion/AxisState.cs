namespace RocketWelder.SDK.Devices.Motion;

/// <summary>
/// The single, explicit state of a motion axis — the PLCopen Motion Control axis state machine
/// (FR-1). There is exactly one state at a time: any convenience boolean
/// (<c>IsReady</c> / <c>IsMoving</c> / <c>IsHomed</c>) is <b>derived</b> from it, never stored
/// alongside it, so a combination such as "homed AND errored AND not ready" cannot be constructed
/// (AC-1).
///
/// <para>
/// <b>Deliberate departure from PLCopen (FR-1).</b> A motion command issued from any state other
/// than <see cref="Standstill"/> is rejected with <see cref="MotionError.Busy"/> and leaves the
/// state <i>intact</i>; PLCopen would drive the axis to <see cref="ErrorStop"/>.
/// <see cref="ErrorStop"/> is reserved for faults, not for caller mistakes — in async C# a rejected
/// <see cref="System.Threading.Tasks.Task"/> is a first-class outcome.
/// </para>
/// </summary>
public enum AxisState
{
    /// <summary>Power is off. <c>MC_Power</c> (<c>PowerAsync(true)</c>) leaves this state.</summary>
    Disabled,

    /// <summary>Powered and at rest. The only state from which a motion command is accepted.</summary>
    Standstill,

    /// <summary>Executing the homing sequence (<c>MC_Home</c>).</summary>
    Homing,

    /// <summary>Executing a move to a target (<c>MC_MoveAbsolute</c> / <c>MC_MoveRelative</c>).</summary>
    DiscreteMotion,

    /// <summary>Turning at a commanded velocity (<c>MC_MoveVelocity</c>); ends only on stop or cancel.</summary>
    ContinuousMotion,

    /// <summary>
    /// Reserved for coordinated motion. No implementation in epic-065 ever enters this state —
    /// the seam is named so that a caller asking for coordinated motion gets a truthful "no"
    /// (see <see cref="AxisCapabilities.Synchronised"/>) rather than a silently degraded move.
    /// </summary>
    SynchronisedMotion,

    /// <summary>Decelerating after <c>MC_Stop</c> or a cancellation; returns to <see cref="Standstill"/> at rest.</summary>
    Stopping,

    /// <summary>A fault is latched. Left only through <c>ResetAsync</c> (<c>MC_Reset</c>).</summary>
    ErrorStop,
}
