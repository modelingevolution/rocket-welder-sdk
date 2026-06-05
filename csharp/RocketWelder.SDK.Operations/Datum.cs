using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Operations;

/// <summary>
/// Program-level registration reference (per <c>data-model.md</c> §1/§2): a part is registered
/// once and all segments share that <c>T_part</c>. Datum is program-level, never per-segment.
/// </summary>
/// <param name="Scheme">
/// Registration scheme: <c>"three-point"</c> (6-DOF, default) or <c>"two-point-plane"</c>
/// (fixture-constrained).
/// </param>
/// <param name="Points">
/// The datum points in <em>touch order</em> (serialized in this order, never re-sorted). 2–3 points.
/// </param>
public sealed record Datum(
    string Scheme,
    IReadOnlyList<DatumPoint> Points);

/// <summary>
/// One datum touch point (per <c>data-model.md</c> §2 <c>datum.points[]</c>).
/// </summary>
/// <param name="Id">Stable point id (e.g. <c>"d0"</c>).</param>
/// <param name="P">Point in model-frame mm.</param>
/// <param name="OnFace">The face id this point lies on, or <c>null</c>.</param>
/// <param name="OnEdge">The edge id this point lies on, or <c>null</c>.</param>
public sealed record DatumPoint(
    string Id,
    Vector3<double> P,
    string? OnFace,
    string? OnEdge);
