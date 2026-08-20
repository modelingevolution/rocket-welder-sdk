namespace RocketWelder.SDK.Devices.Motion;

/// <summary>
/// Machine-readable failure reason (FR-6). A caller decides between reset, re-home and abort by
/// branching on this enum — never by matching on message text (AC-19).
/// </summary>
public enum MotionError
{
    /// <summary>A command was issued while the axis was not in <see cref="AxisState.Standstill"/>.
    /// The axis does not move and its state is left intact (FR-1, AC-2).</summary>
    Busy,

    /// <summary>An absolute move was issued on an unhomed axis that declares
    /// <see cref="AxisCapabilities.Homing"/>.</summary>
    NotHomed,

    /// <summary>The target lies outside <c>Min</c>..<c>Max</c> on a limited (non-wrapping) axis.</summary>
    OutOfRange,

    /// <summary>The requested speed lies outside <c>MinSpeed</c>..<c>MaxSpeed</c>. Rejected, never
    /// clamped — including a <c>Percentage</c> that resolves below the drive's floor (FR-5, AC-6).</summary>
    UnreachableSpeed,

    /// <summary>A <see cref="RotationSense"/> other than <see cref="RotationSense.Shortest"/> was
    /// requested on an axis that does not declare
    /// <see cref="AxisCapabilities.ContinuousRotation"/>.</summary>
    UnsupportedSense,

    /// <summary>A hardware travel limit is active (see <see cref="LimitSwitchState"/>).</summary>
    LimitTripped,

    /// <summary>The drive reported a fault of its own.</summary>
    DriveFault,

    /// <summary>The transport to the drive failed (e.g. the Modbus link dropped).</summary>
    CommunicationLost,

    /// <summary>The dead-commander watchdog tripped and its fault code was read back (FR-11).
    /// Recovery is reset + re-command; the home latch is untouched, so no re-home.</summary>
    WatchdogTripped,

    /// <summary>An axis name did not bind — the facade or the <c>IMotionDevice</c> indexer was given
    /// a name the device does not declare (AC-17).</summary>
    UnknownAxis,

    /// <summary>The declared kind does not match the physical axis, e.g. <c>Rotary(name)</c> on an
    /// axis declared linear.</summary>
    WrongAxisKind,

    /// <summary>Another commander's heartbeat is live on this drive; the FR-11 advisory lease is
    /// held elsewhere. Retry until it expires.</summary>
    LeaseHeld,
}
