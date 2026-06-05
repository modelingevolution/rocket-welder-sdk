namespace RocketWelder.SDK.Operations;

/// <summary>
/// Topological curve kind of a CAD edge, as classified by the geometry service.
/// Serialized lowercase per <c>data-model.md</c> §3.1 (<c>"kind"</c>).
/// </summary>
public enum EdgeKind
{
    /// <summary>Straight line segment.</summary>
    Line,
    /// <summary>Circular arc (open).</summary>
    Arc,
    /// <summary>Full circle (closed).</summary>
    Circle,
    /// <summary>Free-form spline curve.</summary>
    Spline
}
