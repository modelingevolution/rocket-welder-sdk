using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Devices.Positioner;

/// <summary>
/// Complete state of one axis at a point in time.
///
/// <para>
/// Every field is nullable on purpose. When the link to the drive is down only <see cref="Axis"/>
/// and <see cref="Connected"/> are known, and reporting an unknown position as <c>0°</c> is the
/// difference between "at zero" and "no idea" — on a positioner that distinction decides whether
/// it is safe to move.
/// </para>
/// </summary>
/// <param name="Axis">Axis name.</param>
/// <param name="Connected">Link to the drive is up.</param>
/// <param name="Busy">An operation commanded through this API is running.</param>
/// <param name="Operation">Which operation, when <paramref name="Busy"/>.</param>
/// <param name="Ready">Free of faults and errors, homed if required, and idle.</param>
/// <param name="Homed">A homing run has completed since the drive was last powered up.</param>
/// <param name="Angle">Current angle, counted from the captured zero.</param>
/// <param name="Target">Last commanded target, or <c>null</c> after homing or continuous rotation.</param>
/// <param name="Moving">The drive is actually turning.</param>
/// <param name="Direction">Direction while moving.</param>
/// <param name="ServoOn">Drive is energised.</param>
/// <param name="SpeedDegPerSecond">Configured traverse speed for positioning.</param>
/// <param name="ActualSpeedDegPerSecond">Speed the drive is producing right now.</param>
/// <param name="DriveFault">Vendor fault code; 0 means none.</param>
/// <param name="Limits">Limit-switch state, or <c>null</c> on an axis without limits.</param>
/// <param name="HomeSensor">Home (origin) sensor sees its cam.</param>
/// <param name="Error">Description of the last operation failure, or <c>null</c>.</param>
/// <param name="RawPosition">Raw encoder count — diagnostics only.</param>
public sealed record PositionerAxisStatus(
    string Axis,
    bool? Connected,
    bool? Busy,
    PositionerOperation? Operation,
    bool? Ready,
    bool? Homed,
    Degree<double>? Angle,
    Degree<double>? Target,
    bool? Moving,
    RotationDirection? Direction,
    bool? ServoOn,
    double? SpeedDegPerSecond,
    double? ActualSpeedDegPerSecond,
    int? DriveFault,
    LimitSwitchState? Limits,
    bool? HomeSensor,
    string? Error,
    long? RawPosition)
{
    /// <summary>Status for an axis whose drive cannot be reached.</summary>
    public static PositionerAxisStatus Offline(string axis, string error) => new(
        axis, Connected: false, Busy: null, Operation: null, Ready: false, Homed: null,
        Angle: null, Target: null, Moving: null, Direction: null, ServoOn: null,
        SpeedDegPerSecond: null, ActualSpeedDegPerSecond: null, DriveFault: null,
        Limits: null, HomeSensor: null, Error: error, RawPosition: null);
}

/// <summary>State of an axis's end-of-travel switches.</summary>
/// <param name="Min">The lower-travel switch has tripped.</param>
/// <param name="Max">The upper-travel switch has tripped.</param>
public readonly record struct LimitSwitchState(bool Min, bool Max)
{
    /// <summary>True when either switch has tripped.</summary>
    public bool Any => Min || Max;
}

/// <summary>Operation an axis is currently executing.</summary>
public enum PositionerOperation
{
    /// <summary>Homing to capture the zero.</summary>
    Home,

    /// <summary>Moving to an absolute angle.</summary>
    Move,

    /// <summary>Turning continuously.</summary>
    Rotate,
}
