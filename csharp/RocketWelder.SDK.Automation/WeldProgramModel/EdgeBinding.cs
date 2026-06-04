using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Automation.WeldProgramModel;

/// <summary>
/// Durable geometric fingerprint of a CAD edge (NOT an OCCT ordinal). Per
/// <c>data-model.md</c> §3.1. A binding must survive (a) reload of the same STEP and (b) a
/// <em>revised</em> STEP whose topological ordinals shifted, so it stores geometry — not an index —
/// and is re-resolved against the live topology on open by
/// <see cref="EdgeBindingResolver"/>.
/// </summary>
/// <param name="EdgeIdHint">
/// The <c>edgeId</c> recorded at authoring time. A FAST-PATH hint only — never trusted as
/// authoritative; the fingerprint is what binds (§3.2).
/// </param>
/// <param name="Kind">Topological curve kind (line/arc/circle/spline), from topology.</param>
/// <param name="LengthMm">Arclength of the edge in millimetres.</param>
/// <param name="Midpoint">Curve midpoint at t=0.5, model-frame mm.</param>
/// <param name="TangentAtMid">Unit tangent at the midpoint; sign-normalized per §3.3.</param>
/// <param name="Endpoints">Both vertices, sorted lexicographically by (x,y,z) per §3.3. Exactly 2 entries.</param>
/// <param name="AdjFaceNormals">
/// Outward normals of the adjacent faces at the midpoint, sorted by the same canonical rule (§3.3);
/// 2 entries for an interior edge, 1 entry for a boundary edge.
/// </param>
public sealed record EdgeBinding(
    string EdgeIdHint,
    EdgeKind Kind,
    double LengthMm,
    Vector3<double> Midpoint,
    Vector3<double> TangentAtMid,
    IReadOnlyList<Vector3<double>> Endpoints,
    IReadOnlyList<Vector3<double>> AdjFaceNormals);
