namespace RocketWelder.SDK.Devices.Positioner;

/// <summary>
/// An axis operation failed. Carries a machine-readable <see cref="Code"/> alongside the message so
/// callers can branch on the cause instead of matching on text.
/// </summary>
public class PositionerException : Exception
{
    /// <summary>Creates a positioner exception.</summary>
    public PositionerException(PositionerError code, string message, Exception? inner = null)
        : base(message, inner) => Code = code;

    /// <summary>What went wrong, as a stable value.</summary>
    public PositionerError Code { get; }

    /// <summary>Axis the failure belongs to, when known.</summary>
    public string? Axis { get; init; }
}

/// <summary>
/// Stable failure causes. These decide what a caller does next — reset, re-home, or give up — so
/// they are part of the contract and must not be renumbered or repurposed.
/// </summary>
public enum PositionerError
{
    /// <summary>Cause not covered by a more specific value.</summary>
    Unknown = 0,

    /// <summary>Transport to the drive failed.</summary>
    CommunicationFailed = 1,

    /// <summary>Absolute positioning attempted before homing.</summary>
    NotHomed = 2,

    /// <summary>An end-of-travel switch tripped.</summary>
    LimitTripped = 3,

    /// <summary>The drive reported a fault; clear it with a reset.</summary>
    DriveFault = 4,

    /// <summary>Commanded but not turning — jammed, unpowered, or below breakaway speed.</summary>
    Stalled = 5,

    /// <summary>Operation exceeded its time budget.</summary>
    Timeout = 6,

    /// <summary>Motion finished outside tolerance.</summary>
    PositionNotReached = 7,

    /// <summary>The home sensor was found but the drive never latched the position.</summary>
    HomeLatchFailed = 8,

    /// <summary>Cancelled by the caller or by a stop command.</summary>
    Aborted = 9,

    /// <summary>Another operation is already running on this axis.</summary>
    Busy = 10,

    /// <summary>
    /// The positive direction moves the axis the wrong way — positioning would drive away from the
    /// target. Re-wire the motor or invert the axis in configuration.
    /// </summary>
    DirectionInverted = 11,
}
