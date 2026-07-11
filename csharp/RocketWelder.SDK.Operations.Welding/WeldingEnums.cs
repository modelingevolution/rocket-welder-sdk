namespace RocketWelder.SDK.Operations;

/// <summary>
/// Seam type of a weld (per <c>data-model.md</c> §2 <c>segment.seamType</c> and the §2 "Enums" block):
/// <c>fillet | butt | edge | lap | corner | circumferential</c>. Serialized lowercase.
/// </summary>
public enum SeamType
{
    /// <summary>Fillet weld (T-joint / lap leg).</summary>
    Fillet,
    /// <summary>Butt weld (groove).</summary>
    Butt,
    /// <summary>Edge weld.</summary>
    Edge,
    /// <summary>Lap weld.</summary>
    Lap,
    /// <summary>Corner weld.</summary>
    Corner,
    /// <summary>Circumferential (closed-loop) weld.</summary>
    Circumferential
}

/// <summary>
/// ISO 6947 welding position (per <c>data-model.md</c> §2 <c>segment.position</c> and the §2 "Enums" block):
/// <c>PA..PG</c>. Stored first-class; drives the procedure (D11). Serialized as the two-letter code.
/// </summary>
public enum WeldPosition
{
    /// <summary>Flat (downhand).</summary>
    PA,
    /// <summary>Horizontal-vertical.</summary>
    PB,
    /// <summary>Horizontal.</summary>
    PC,
    /// <summary>Horizontal overhead.</summary>
    PD,
    /// <summary>Overhead.</summary>
    PE,
    /// <summary>Vertical-up.</summary>
    PF,
    /// <summary>Vertical-down.</summary>
    PG
}

/// <summary>
/// The deposition role of a pass within a multi-pass seam (per <c>data-model.md</c> §2
/// <c>passes[].role</c> and the §2 "Enums" block): <c>root | hot | fill | cap</c>. Serialized lowercase.
/// </summary>
public enum PassRole
{
    /// <summary>Root pass (first bead).</summary>
    Root,
    /// <summary>Hot pass (over the root).</summary>
    Hot,
    /// <summary>Fill pass(es).</summary>
    Fill,
    /// <summary>Cap (final) pass.</summary>
    Cap
}

/// <summary>
/// Torch technique (per <c>data-model.md</c> §2 <c>toolFrame.technique</c> and the §2 "Enums" block):
/// <c>push | drag | perpendicular</c> — sets the travel-angle sign. Serialized lowercase.
/// </summary>
public enum Technique
{
    /// <summary>Push (forehand) technique.</summary>
    Push,
    /// <summary>Drag (backhand) technique.</summary>
    Drag,
    /// <summary>Perpendicular (no travel lead/lag).</summary>
    Perpendicular
}

/// <summary>
/// Electrode polarity (per <c>data-model.md</c> §2 <c>segment.polarity</c> and the §2 "Enums" block):
/// <c>DCEP | DCEN | AC</c>. Serialized verbatim (upper-case codes).
/// </summary>
public enum Polarity
{
    /// <summary>Direct current, electrode positive.</summary>
    DCEP,
    /// <summary>Direct current, electrode negative.</summary>
    DCEN,
    /// <summary>Alternating current.</summary>
    AC
}

/// <summary>
/// Runtime per-segment refinement mode (per <c>data-model.md</c> §2 <c>resolver.mode</c> and the §2
/// "Enums" block): <c>metrology | adjacent-feature | laser-profile | touch-only | seam-tracking</c>.
/// Serialized lowercase, hyphenated.
/// </summary>
public enum ResolverMode
{
    /// <summary>Metrology-driven refinement.</summary>
    Metrology,
    /// <summary>Track a visible adjacent feature.</summary>
    AdjacentFeature,
    /// <summary>Laser-profile refinement.</summary>
    LaserProfile,
    /// <summary>Touch-sensing only.</summary>
    TouchOnly,
    /// <summary>In-weld seam tracking.</summary>
    SeamTracking
}

/// <summary>
/// In-weld tracking sensor mode (per <c>data-model.md</c> §2 <c>resolver.tracking.mode</c> and the §2
/// "Enums" block): <c>tast | laser | touch</c>. Serialized lowercase.
/// </summary>
public enum TrackingMode
{
    /// <summary>Through-arc seam tracking (TAST).</summary>
    Tast,
    /// <summary>Laser seam tracking.</summary>
    Laser,
    /// <summary>Touch-based tracking.</summary>
    Touch
}
