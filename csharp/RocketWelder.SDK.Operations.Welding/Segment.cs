namespace RocketWelder.SDK.Operations;

/// <summary>
/// One weld along one bound edge — the weld specialization of the shared FeaturePlan concept
/// (per <c>data-model.md</c> §1/§2). Carries the generic <see cref="Binding"/> + <see cref="SubRange"/> +
/// <see cref="Resolver"/> (the Epic-029-shared, welding-agnostic part) PLUS segment-level welding facts
/// (seam type, position, weld size, gas, polarity) and an ordered <see cref="Passes"/> list (≥1).
/// The position of a Segment in <see cref="WeldProgram.Segments"/> <em>is</em> its weld order (§2 rule 2).
/// </summary>
/// <param name="Id">Stable Segment id — the diff/edit identity (e.g. <c>"s0"</c>).</param>
/// <param name="Binding">The durable edge fingerprint this weld is bound to (§3).</param>
/// <param name="SubRange">[t0,t1] along the bound edge (0..1 arclength).</param>
/// <param name="SeamType">Seam type (fillet | butt | edge | lap | corner | circumferential).</param>
/// <param name="Position">
/// ISO 6947 welding position (PA..PG) — stored, first-class; drives the procedure (D11). <c>null</c>
/// when unauthored (a <c>/1</c> migration discarded it after templating — §4.2 GAP-4; never invented).
/// </param>
/// <param name="WeldSize">
/// Weld-symbol dimensional facts (D14): fillet OR butt. <c>null</c> when unauthored (§4.2; never invented).
/// </param>
/// <param name="Gas">Shielding-gas fact (D14): mix + flow. <c>null</c> when unauthored (§4.2; never invented).</param>
/// <param name="Polarity">Electrode polarity (DCEP | DCEN | AC) — stored (D14). <c>null</c> when unauthored (§4.2).</param>
/// <param name="Passes">
/// The ordered pass list (≥1). The array index <em>is</em> the deposition order (root→…→cap);
/// the serializer never re-sorts passes (§2 rule 2). A single-pass program is length 1 (v1 shape).
/// </param>
/// <param name="Resolver">Runtime per-segment refinement policy; may be <c>null</c>.</param>
/// <param name="ExternalAxis">Optional positioner setpoint (D17); <c>null</c> when absent.</param>
public sealed record Segment(
    string Id,
    EdgeBinding Binding,
    SubRange SubRange,
    SeamType SeamType,
    WeldPosition? Position,
    WeldSize? WeldSize,
    Gas? Gas,
    Polarity? Polarity,
    IReadOnlyList<Pass> Passes,
    SegmentResolver? Resolver,
    ExternalAxis? ExternalAxis);

/// <summary>
/// The [t0,t1] arclength sub-range (0..1) of the bound edge that this segment welds.
/// </summary>
/// <param name="T0">Start parameter, 0..1.</param>
/// <param name="T1">End parameter, 0..1.</param>
public readonly record struct SubRange(double T0, double T1);

/// <summary>
/// One deposition pass in a seam (per <c>data-model.md</c> §2 <c>passes[]</c>). Each pass owns its
/// own motion (<see cref="ToolFrame"/> + <see cref="Motion"/>) and a <see cref="JobRef"/> — the welder
/// job that <em>is</em> the qualified procedure for that pass (D-D). Pass order is positional (§2 rule 2).
/// </summary>
/// <param name="Id">Stable Pass id — the diff identity across a reorder (e.g. <c>"p0"</c>).</param>
/// <param name="Role">Deposition role (root | hot | fill | cap).</param>
/// <param name="JobRef">Reference to the welder job that is the qualified procedure for this pass (D-D).</param>
/// <param name="ToolFrame">TCP relative to the seam (platform ToolFrame, ex-TorchFrame).</param>
/// <param name="Motion">Platform motion profile (travel speed + optional weave).</param>
public sealed record Pass(
    string Id,
    PassRole Role,
    JobRef JobRef,
    ToolFrame ToolFrame,
    MotionProfile Motion);

/// <summary>
/// Reference to the welder JOB number/id = the QUALIFIED procedure (per <c>data-model.md</c> §2
/// <c>passes[].jobRef</c>, D-D). REFERENCE ONLY: the job's electrical setpoints (WFS / arc voltage /
/// transfer mode / pulse program) live IN THE DEVICE, NOT here. We reference, we do not store them.
/// </summary>
/// <param name="Id">The welder job number/id.</param>
public sealed record JobRef(int Id);

/// <summary>
/// TCP orientation relative to the seam (per <c>data-model.md</c> §2 <c>passes[].toolFrame</c>) — the
/// platform <c>ToolFrame</c> (ex-<c>TorchFrame</c>).
/// </summary>
/// <param name="StandoffMm">Standoff distance (= offsetAlongNormal from the generic gizmo; CTWD).</param>
/// <param name="WorkAngleDeg">Work angle (= rotAboutTangent).</param>
/// <param name="TravelAngleDeg">Travel angle (= rotAboutNormal).</param>
/// <param name="Technique">Torch technique: push | drag | perpendicular (sets travel-angle sign).</param>
public sealed record ToolFrame(
    double StandoffMm,
    double WorkAngleDeg,
    double TravelAngleDeg,
    Technique Technique);

/// <summary>
/// Platform motion profile (per <c>data-model.md</c> §2 <c>passes[].motion</c>) — pure motion, no
/// welding words.
/// </summary>
/// <param name="TravelSpeedMmPerS">Travel speed in mm/s (authored robot motion).</param>
/// <param name="Weave">Weave oscillation; <c>null</c> = stringer.</param>
public sealed record MotionProfile(
    double TravelSpeedMmPerS,
    Weave? Weave);

/// <summary>
/// Weave oscillation parameters (per <c>data-model.md</c> §2 <c>motion.weave</c>).
/// </summary>
/// <param name="AmplitudeMm">Peak-to-side amplitude in mm.</param>
/// <param name="FrequencyHz">Oscillation frequency in Hz.</param>
/// <param name="EdgeDwellMs">Dwell time at each edge of the weave, in ms.</param>
/// <param name="Pattern">Weave pattern identifier (e.g. <c>"triangle"</c>, <c>"sine"</c>).</param>
public sealed record Weave(
    double AmplitudeMm,
    double FrequencyHz,
    double EdgeDwellMs,
    string Pattern);

/// <summary>
/// Weld-symbol dimensional facts (per <c>data-model.md</c> §2 <c>segment.weldSize</c>, D14): exactly one
/// of <see cref="Fillet"/> OR <see cref="Butt"/> describes the weld; the other is <c>null</c>.
/// </summary>
/// <param name="Fillet">Fillet dimension (leg OR throat), or <c>null</c>.</param>
/// <param name="Butt">Butt-groove dimensions, or <c>null</c>.</param>
public sealed record WeldSize(
    FilletSize? Fillet,
    ButtSize? Butt);

/// <summary>
/// Fillet weld dimension (per <c>data-model.md</c> §2 <c>weldSize.fillet</c>): leg OR throat.
/// Exactly one of <see cref="LegMm"/> / <see cref="ThroatMm"/> is set; the other is <c>null</c>.
/// </summary>
/// <param name="LegMm">Fillet leg length in mm, or <c>null</c>.</param>
/// <param name="ThroatMm">Fillet throat in mm, or <c>null</c>.</param>
public sealed record FilletSize(
    double? LegMm,
    double? ThroatMm);

/// <summary>
/// Butt-groove dimensions (per <c>data-model.md</c> §2 <c>weldSize.butt</c>).
/// </summary>
/// <param name="GrooveAngleDeg">Included groove angle in degrees.</param>
/// <param name="RootGapMm">Root gap in mm.</param>
/// <param name="RootFaceMm">Root face (land) in mm.</param>
/// <param name="Prep">Edge preparation (e.g. <c>"single-V"</c> | <c>"double-V"</c>).</param>
public sealed record ButtSize(
    double GrooveAngleDeg,
    double RootGapMm,
    double RootFaceMm,
    string Prep);

/// <summary>
/// Shielding-gas fact (per <c>data-model.md</c> §2 <c>segment.gas</c>, D14).
/// </summary>
/// <param name="Mix">Gas mix label (e.g. <c>"80/20 Ar/CO2"</c>).</param>
/// <param name="FlowLpm">Flow rate in litres/min.</param>
public sealed record Gas(
    string Mix,
    double FlowLpm);

/// <summary>
/// Runtime per-segment refinement policy (per <c>data-model.md</c> §2 <c>segment.resolver</c>).
/// Governs runtime refinement only — never the program-level base registration (§1).
/// </summary>
/// <param name="Mode">Resolver mode (metrology | adjacent-feature | laser-profile | touch-only | seam-tracking).</param>
/// <param name="FeatureRef">For <c>adjacent-feature</c>: the visible edge to track. May be <c>null</c>.</param>
/// <param name="Tracking">Optional in-weld tracking policy (D16); <c>null</c> when absent.</param>
public sealed record SegmentResolver(
    ResolverMode Mode,
    string? FeatureRef,
    Tracking? Tracking);

/// <summary>
/// In-weld tracking policy (per <c>data-model.md</c> §2 <c>resolver.tracking</c>, D16).
/// </summary>
/// <param name="Mode">Tracking sensor mode (tast | laser | touch).</param>
/// <param name="GapFill">Whether adaptive gap-fill is enabled.</param>
public sealed record Tracking(
    TrackingMode Mode,
    bool GapFill);

/// <summary>
/// Optional positioner / external-axis setpoint (per <c>data-model.md</c> §2 <c>segment.externalAxis</c>, D17).
///
/// <para>
/// A weld-program <b>setpoint</b>, not an API call (epic-065 FR-9): the executor positions the named
/// axis and waits for standstill before the arc strikes — indexed motion, the positioner is
/// stationary while welding. Identity is by <b>name on both halves</b>, so the declarative weld
/// program and the procedural automation program meet only at frozen identifiers.
/// </para>
///
/// <para>
/// <b>Flagged, not solved here:</b> <paramref name="AngleDeg"/> is rotary-only. The day a linear
/// track joins weld programs, the setpoint needs a unit story consistent with the SDK's typed
/// motion contract.
/// </para>
/// </summary>
/// <param name="Device">The <b>cell-role</b> identifier of the motion device — <c>"positioner"</c>,
/// <c>"track"</c> — fixed per cell standard and resolved through the motion contract's kind handles.
/// Never the vendor discriminator, so a vendor swap that redeclares the same axis names does not
/// break stored programs.</param>
/// <param name="Axis">The plugin-frozen axis name (<c>"tilt"</c>, <c>"turntable"</c>) declared by
/// the device plugin's axis roster.</param>
/// <param name="AngleDeg">The axis setpoint angle in degrees.</param>
public sealed record ExternalAxis(
    string Device,
    string Axis,
    double AngleDeg);
