using System.Text.Json;

namespace RocketWelder.SDK.Automation.WeldProgramModel;

/// <summary>
/// One weld along one bound edge — the weld specialization of the shared FeaturePlan concept
/// (per <c>data-model.md</c> §1/§2). Carries the generic <see cref="Binding"/> + sub-range +
/// resolver plus a weld <see cref="Process"/> and <see cref="TorchFrame"/>.
/// The position of a Segment in <see cref="WeldProgram.Segments"/> <em>is</em> its weld order.
/// </summary>
/// <param name="Id">Stable Segment id — the diff/edit identity (e.g. <c>"s0"</c>).</param>
/// <param name="Binding">The durable edge fingerprint this weld is bound to.</param>
/// <param name="SubRange">[t0,t1] along the bound edge (0..1 arclength).</param>
/// <param name="Process">Weld process (seam type, weld job, travel speed) — rw2/IWeldingMachine domain.</param>
/// <param name="TorchFrame">TCP relative to the seam (rw2 domain).</param>
/// <param name="Resolver">Runtime per-segment refinement policy; may be <c>null</c>.</param>
public sealed record Segment(
    string Id,
    EdgeBinding Binding,
    SubRange SubRange,
    WeldProcess Process,
    TorchFrame TorchFrame,
    SegmentResolver? Resolver);

/// <summary>
/// The [t0,t1] arclength sub-range (0..1) of the bound edge that this segment welds.
/// </summary>
/// <param name="T0">Start parameter, 0..1.</param>
/// <param name="T1">End parameter, 0..1.</param>
public readonly record struct SubRange(double T0, double T1);

/// <summary>
/// Weld process parameters (per <c>data-model.md</c> §2 <c>segment.process</c>) — the
/// rw2 / IWeldingMachine welding-only domain.
/// </summary>
/// <param name="SeamType">Seam type: <c>butt | fillet | edge | circumferential | …</c>.</param>
/// <param name="WeldJob">The synergic line / job reference + parameters.</param>
/// <param name="TravelSpeedMmPerS">Travel speed in mm/s.</param>
public sealed record WeldProcess(
    string SeamType,
    WeldJob WeldJob,
    double TravelSpeedMmPerS);

/// <summary>
/// A weld job / synergic line reference (per <c>data-model.md</c> §2 <c>process.weldJob</c>).
/// </summary>
/// <param name="Id">Numeric job id on the machine.</param>
/// <param name="Params">
/// Opaque synergic-line / job parameters. Serialized verbatim as a nested JSON object, key order
/// preserved as authored (this is machine-owned data the canonical serializer does not reshape).
/// </param>
public sealed record WeldJob(
    int Id,
    IReadOnlyDictionary<string, JsonElement> Params);

/// <summary>
/// TCP orientation relative to the seam (per <c>data-model.md</c> §2 <c>segment.torchFrame</c>).
/// </summary>
/// <param name="StandoffMm">Standoff distance (= offsetAlongNormal from the generic gizmo).</param>
/// <param name="WorkAngleDeg">Work angle (= rotAboutTangent).</param>
/// <param name="TravelAngleDeg">Travel angle (= rotAboutNormal).</param>
/// <param name="Technique">Torch technique: <c>push | drag | perpendicular</c> (sets travel-angle sign).</param>
public sealed record TorchFrame(
    double StandoffMm,
    double WorkAngleDeg,
    double TravelAngleDeg,
    string Technique);

/// <summary>
/// Runtime per-segment refinement policy (per <c>data-model.md</c> §2 <c>segment.resolver</c>).
/// Governs runtime refinement only — never the program-level base registration (§1).
/// </summary>
/// <param name="Mode">Resolver mode: <c>metrology | adjacent-feature | laser-profile | touch-only</c>.</param>
/// <param name="FeatureRef">For <c>adjacent-feature</c>: the visible edge to track. May be <c>null</c>.</param>
public sealed record SegmentResolver(
    string Mode,
    string? FeatureRef);
