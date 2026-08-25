namespace RocketWelder.SDK.Devices.Motion;

/// <summary>
/// Machine-readable failure reason (FR-6). A caller decides between reset, re-home and abort by
/// branching on this enum — never by matching on message text (AC-19).
///
/// <para>
/// <b>The ordinals are written down on purpose, and growth is append-only.</b> This enum crosses
/// process and storage boundaries, so a value can be persisted or in flight while versions differ.
/// Renumbering a member would silently turn every stored value of that number into a different
/// failure — and "reset the drive" for what was an open guard is exactly the wrong thing to tell an
/// operator. Add at the end, never in the middle, and never reorder.
/// </para>
/// </summary>
public enum MotionError
{
    /// <summary>A command was issued while the axis was not in <see cref="AxisState.Standstill"/>.
    /// The axis does not move and its state is left intact (FR-1, AC-2).</summary>
    Busy = 0,

    /// <summary>An absolute move was issued on an unhomed axis that declares
    /// <see cref="AxisCapabilities.Homing"/>.</summary>
    NotHomed = 1,

    /// <summary>The target lies outside <c>Min</c>..<c>Max</c> on a limited (non-wrapping) axis.</summary>
    OutOfRange = 2,

    /// <summary>The requested speed lies outside <c>MinSpeed</c>..<c>MaxSpeed</c>. Rejected, never
    /// clamped — including a <c>Percentage</c> that resolves below the drive's floor (FR-5, AC-6).</summary>
    UnreachableSpeed = 3,

    /// <summary>A <see cref="RotationSense"/> other than <see cref="RotationSense.Shortest"/> was
    /// requested on an axis that does not declare
    /// <see cref="AxisCapabilities.ContinuousRotation"/>.</summary>
    UnsupportedSense = 4,

    /// <summary>A hardware travel limit is active (see <see cref="LimitSwitchState"/>).</summary>
    LimitTripped = 5,

    /// <summary>The drive reported a fault of its own and will not accept motion again until it
    /// is reset at drive level.
    ///
    /// <para>
    /// This is the member for a fault the <b>drive</b> raised. When the drive stayed healthy and
    /// only the mechanism failed to follow — a stall, a positioning timeout, a stop outside
    /// tolerance — the honest member is <see cref="MotionFailed"/>, and a stop that came from the
    /// machine's safety circuit is <see cref="SafetyStop"/>. Sending an operator to reset a drive
    /// that has nothing wrong with it is the failure this boundary exists to prevent.
    /// </para></summary>
    DriveFault = 6,

    /// <summary>The transport to the drive failed (e.g. the Modbus link dropped).</summary>
    CommunicationLost = 7,

    /// <summary>The dead-commander watchdog tripped and its fault code was read back (FR-11).
    /// Recovery is reset + re-command; the home latch is untouched, so no re-home.</summary>
    WatchdogTripped = 8,

    /// <summary>An axis name did not bind — the facade or the <c>IMotionDevice</c> indexer was given
    /// a name the device does not declare (AC-17).</summary>
    UnknownAxis = 9,

    /// <summary>The declared kind does not match the physical axis, e.g. <c>Rotary(name)</c> on an
    /// axis declared linear.</summary>
    WrongAxisKind = 10,

    /// <summary>Another commander's heartbeat is live on this drive; the FR-11 advisory lease is
    /// held elsewhere. Retry until it expires.</summary>
    LeaseHeld = 11,

    /// <summary>The command was accepted and the drive stayed healthy, but the motion did not
    /// achieve what it was told to: the axis stalled, the positioning deadline elapsed, or it came
    /// to rest outside the commanded tolerance.
    ///
    /// <para>
    /// <b>Deliberately not <see cref="DriveFault"/>.</b> There is no drive fault to reset here —
    /// the drive did as it was told and the mechanism did not follow. The operator clears the
    /// mechanical cause (an obstruction, a binding or cold gearbox, a load beyond the axis's
    /// torque, a target the configured speed cannot reach inside the timeout) and re-commands. The
    /// home reference is untouched, so no re-home is needed unless the axis was pushed off
    /// position.
    /// </para></summary>
    MotionFailed = 12,

    /// <summary>Homing found the home sensor but the reference position was never captured — the
    /// position latch did not fire, so the axis has no zero and remains unhomed.
    ///
    /// <para>
    /// <b>Do not offer reset-and-retry.</b> The mechanism did its part and the capture did not
    /// happen, so re-running <c>Home</c> reproduces the same result. The fault lies in the
    /// machine's control program or the sensor wiring behind it — on a PLC-mediated axis, the
    /// home-latch network in the ladder. Escalate to maintenance or commissioning. Reported apart
    /// from <see cref="MotionFailed"/> because the remedy is a different person's job.
    /// </para></summary>
    HomeLatchFailed = 13,

    /// <summary>The machine's safety circuit stopped the motion — an open guard or interlock, a
    /// latched emergency stop, a light curtain or area scanner tripped, or a controller-level
    /// protective stop (e.g. Fairino controller code 99).
    ///
    /// <para>
    /// Neither a drive fault nor a caller mistake, and the remedy is neither of theirs:
    /// <see cref="DriveFault"/> sends an operator to reset a drive, which is the wrong instruction
    /// when the real cause is an open guard, a latched e-stop or a person standing in the cell. The
    /// condition is cleared and acknowledged <b>at the safety circuit</b>, not at the axis, and only
    /// then is the move re-commanded. Motion stays refused while the circuit is open, so retrying
    /// without clearing it simply fails again.
    /// </para></summary>
    SafetyStop = 14,
}
