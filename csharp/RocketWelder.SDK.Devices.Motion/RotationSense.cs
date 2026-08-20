namespace RocketWelder.SDK.Devices.Motion;

/// <summary>
/// Which way round a rotary axis reaches an absolute target. Meaningful <b>only</b> where two paths
/// to that target exist, i.e. on an axis declaring
/// <see cref="AxisCapabilities.ContinuousRotation"/>; on any other axis a value other than
/// <see cref="Shortest"/> is <b>rejected</b> with <see cref="MotionError.UnsupportedSense"/> —
/// never silently ignored — FR-5's philosophy: a value the axis cannot honour is rejected, never
/// quietly reinterpreted.
///
/// <para>
/// This is a shortest-path <i>hint</i> on <c>MoveAbsoluteAsync</c>, not a direction: the deleted
/// <c>RotationDirection</c> meant two different things on an inverted axis (P-2). Direction is
/// carried by the <b>sign</b> of a velocity or of a relative delta.
/// </para>
/// </summary>
public enum RotationSense
{
    /// <summary>Take whichever path is shorter. The only sense a non-wrapping axis accepts.</summary>
    Shortest,

    /// <summary>Reach the target travelling in the axis's positive sense.</summary>
    Positive,

    /// <summary>Reach the target travelling in the axis's negative sense.</summary>
    Negative,
}
