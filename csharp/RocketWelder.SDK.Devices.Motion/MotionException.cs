namespace RocketWelder.SDK.Devices.Motion;

/// <summary>
/// The failure type of the motion contract. Carries a machine-readable <see cref="Error"/> next to
/// the human message so a caller branches on the enum and never on the text (FR-6, AC-19), and the
/// <see cref="AxisName"/> the failure belongs to when one applies.
/// </summary>
public sealed class MotionException : Exception
{
    /// <summary>Creates a motion failure.</summary>
    /// <param name="error">The machine-readable reason a caller branches on.</param>
    /// <param name="message">The human-readable description; never the branching surface.</param>
    /// <param name="axisName">The plugin-frozen axis name this failure belongs to, when one applies.</param>
    public MotionException(MotionError error, string message, string? axisName = null)
        : base(message)
    {
        Error = error;
        AxisName = axisName;
    }

    /// <summary>The machine-readable reason for the failure.</summary>
    public MotionError Error { get; }

    /// <summary>The plugin-frozen axis name this failure belongs to, or <see langword="null"/>
    /// for a device-level failure.</summary>
    public string? AxisName { get; }
}
