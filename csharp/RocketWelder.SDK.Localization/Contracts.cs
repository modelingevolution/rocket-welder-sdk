using ModelingEvolution.Drawing;
using RocketWelder.SDK.Vision;

namespace RocketWelder.SDK.Localization;

// =============================================================================
// IResolver contract (metrology-interface.md §2 ; feature-plan-binding.md §4).
//
// Feature-agnostic localization contracts. Ported from the rw2 Metrology
// Contracts.cs (SDK-IT-3g): the shapes are verbatim, in feature language
// (MetrologyRequest.Feature, ResolveResult.FeatureConfidence), with the image/camera
// inputs typed against SDK contracts (IGrayImage, CameraIntrinsics + camera world
// pose) so no imaging back-end leaks into this package.
// =============================================================================

/// <summary>The general resolver contract (metrology-interface.md §2). Every
/// VisionResolver / TouchOnlyResolver / MetrologyResolver fills the same shape.</summary>
public interface IResolver
{
    /// <summary>Refine the coarse part pose and produce a per-waypoint deviation field
    /// from sensed (here: lit multi-view) data. Never fabricates a pose: returns a
    /// non-Ok <see cref="ResolveStatus"/> and low <see cref="ResolveResult.FeatureConfidence"/>
    /// when the data cannot support a localization (metrology-interface.md §5).</summary>
    Task<ResolveResult> ResolveAsync(MetrologyRequest request, CancellationToken ct = default);
}

/// <summary>metrology-interface.md §2. The metrology-specific input.</summary>
public sealed record MetrologyRequest(
    Pose3<double> CoarseTPart,                       // the prior (touch-registration.md)
    string ShapeId,                                  // STEP loaded server-side (integration-contract Layer B)
    FeatureBinding Feature,                          // which CAD edge/feature to localize (model frame)
    IReadOnlyList<MetrologyView> Views,              // captured lit frames + their camera pose/intrinsics (>=3)
    IlluminationSpec Lighting);                      // the grazing/structured rig that gives contrast (§4)

/// <summary>One captured lit frame: the gray image plus the camera world pose and
/// intrinsics it was taken with. The image is carried as the SDK gray-image seam
/// (<see cref="IGrayImage"/>) and the camera as <see cref="CameraIntrinsics"/> plus its
/// world pose (<see cref="CameraTWorld"/>), so this contract stays free of any imaging
/// back-end. (In the consumer this is produced by the capture loop;
/// metrology-interface.md §3 steps 1-2.)</summary>
public sealed record MetrologyView(
    IGrayImage Image,
    CameraIntrinsics Intrinsics,
    Pose3<double> CameraTWorld);

/// <summary>metrology-interface.md §2. The general IResolver return.</summary>
public sealed record ResolveResult(
    Pose3<double> ExactTPart,                        // refined CAD->robot pose
    DeviationField Deviation,                        // per-waypoint correction, model frame
    double FeatureConfidence,                        // 0..1 ; LOW => not localizable (§5)
    ResolveStatus Status);                           // Ok | LowContrast | InsufficientViews | OutOfSearchWindow

public enum ResolveStatus { Ok, LowContrast, InsufficientViews, OutOfSearchWindow }

/// <summary>metrology-interface.md §2. Per-waypoint deviation, model frame.
/// <see cref="WaypointDeltas"/> is aligned 1:1 to the feature's sampled waypoints; an
/// entry is null where that waypoint could not be triangulated (honest gap, not zero).</summary>
public sealed record DeviationField(
    IReadOnlyList<Vector3<double>?> WaypointDeltas,
    double Rms,
    double P90);

/// <summary>Which CAD edge/feature to localize, sampled into waypoints in the model
/// frame (metrology-interface.md MetrologyRequest.Feature). For IT-6 we carry the sampled
/// waypoints + their unit tangents directly (the production binding resolves these from
/// the geometry service's /sample-edge).</summary>
public sealed record FeatureBinding(
    string EdgeId,
    IReadOnlyList<Vector3<double>> Waypoints,        // model frame, metres
    IReadOnlyList<Vector3<double>> Tangents);        // unit edge tangents, model frame

/// <summary>metrology-interface.md §2/§4. Lighting is a REQUIRED input — the POC proved
/// the feature has zero passive contrast under diffuse light.</summary>
public sealed record IlluminationSpec(
    IlluminationKind Kind,
    IReadOnlyList<DirectionalLight>? Lights = null,
    string? SimLightsSpec = null);

public enum IlluminationKind { Diffuse, Grazing, Structured, SimOverride }

public sealed record DirectionalLight(Vector3<double> Direction, float R, float G, float B, float Intensity);
