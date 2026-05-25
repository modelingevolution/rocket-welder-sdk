using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Automation.AdaptivePoints;

/// <summary>
/// Live handle to one captured adaptive-point, bound to the program's active robot + camera.
/// Wraps the stored record and adds run-time behaviour (<see cref="AdaptAsync"/>).
/// Obtained via <c>ctx.GetAdaptivePoint(name)</c>.
/// </summary>
public interface IAdaptivePoint
{
    /// <summary>Stable name within the robot's catalogue.</summary>
    string Name { get; }

    /// <summary>Name of the controller teach-point this adaptive-point is bound to.</summary>
    string TeachPointName { get; }

    /// <summary>P — the operator-taught master pose.</summary>
    Pose3<double> TaughtPose { get; }

    /// <summary>δ = P − V_master — the constant visual-offset (diagnostic).</summary>
    Vector3<double> Delta { get; }

    /// <summary>Segmentation class id this adaptive-point tracks.</summary>
    byte FeatureClassId { get; }

    /// <summary>When this adaptive-point was first captured.</summary>
    DateTimeOffset CapturedAt { get; }

    /// <summary>
    /// Drives the cobot to the master camera-pose, photographs the current part, detects
    /// the feature, and computes the corrected pose <c>P_k = V_k + δ</c>. Failure is a
    /// return value — see <see cref="AdaptResult"/> for the five exhaustive cases.
    /// </summary>
    Task<AdaptResult> AdaptAsync(CancellationToken ct = default);

    /// <summary>The last outcome from <see cref="AdaptAsync"/> in this run, or null until adapted.</summary>
    AdaptResult? Current { get; }
}
