using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.AdaptivePoints;

/// <summary>
/// Outcome of <see cref="IAdaptivePoint.AdaptAsync"/>. Five exhaustive cases; failure is a
/// return value, never an exception. Caller pattern-matches and steers.
/// </summary>
public abstract record AdaptResult
{
    private AdaptResult() { }

    /// <summary>Adaptation succeeded. <paramref name="Pose"/> is the corrected teach-point P_k.</summary>
    /// <param name="Pose">Corrected teach-point: P_k = V_k + δ.</param>
    /// <param name="Correction">Δ_k = V_k − V_master — the part-to-part shift.</param>
    public sealed record Ok(Pose3<double> Pose, Vector3<double> Correction) : AdaptResult;

    /// <summary>Calibration has advanced since this adaptive-point was captured. Re-capture or recalibrate.</summary>
    public sealed record Stale(string Reason) : AdaptResult;

    /// <summary>Camera returned no frame at the photo pose.</summary>
    public sealed record NoFrame : AdaptResult;

    /// <summary>AI segmentation returned no polygon for the configured feature class.</summary>
    public sealed record NoDetection : AdaptResult;

    /// <summary>‖Δ_k‖ exceeded MaxCorrectionMm — probable wrong-family part.</summary>
    public sealed record OutOfRange(Vector3<double> Attempted) : AdaptResult;
}
