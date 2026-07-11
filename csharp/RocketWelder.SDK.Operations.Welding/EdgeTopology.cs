using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Operations;

/// <summary>
/// SDK-side mirror of the geometry service's per-edge topology (the subset the binding needs;
/// see <c>integration-contract.md</c> §3: <c>polyline</c>, <c>length</c>, <c>adjacentFaces</c>).
/// A POCO so IT-1 stays headless and does NOT depend on the simulator / native geometry.
/// </summary>
/// <param name="EdgeId">The edge id in the live topology (an OCCT ordinal — may shift across STEP revisions).</param>
/// <param name="Kind">Topological curve kind.</param>
/// <param name="Polyline">
/// Densely-sampled polyline of the edge in model-frame mm, ordered from one vertex to the other.
/// Used to derive length, midpoint, tangent and endpoints.
/// </param>
/// <param name="Length">Arclength of the edge in millimetres.</param>
/// <param name="AdjacentFaceNormals">
/// Outward normals of the faces adjacent to this edge at the midpoint; 2 for an interior edge,
/// 1 for a boundary edge.
/// </param>
public sealed record EdgeTopology(
    string EdgeId,
    EdgeKind Kind,
    IReadOnlyList<Vector3<double>> Polyline,
    double Length,
    IReadOnlyList<Vector3<double>> AdjacentFaceNormals);

/// <summary>
/// The live topology of a loaded STEP: the set of edges plus the part bounding-box diagonal
/// used to scale-normalize the midpoint distance term (<c>data-model.md</c> §3.3).
/// </summary>
public sealed class Topology
{
    private readonly Dictionary<string, EdgeTopology> _byId;

    /// <summary>All edges in the loaded shape.</summary>
    public IReadOnlyList<EdgeTopology> Edges { get; }

    /// <summary>Part bounding-box diagonal in mm (used to normalize the midpoint distance term).</summary>
    public double BBoxDiagonalMm { get; }

    /// <summary>Creates a topology from its edges and (optionally) an explicit bbox diagonal.</summary>
    public Topology(IReadOnlyList<EdgeTopology> edges, double? bboxDiagonalMm = null)
    {
        ArgumentNullException.ThrowIfNull(edges);
        Edges = edges;
        _byId = new Dictionary<string, EdgeTopology>(edges.Count);
        foreach (var e in edges)
            _byId[e.EdgeId] = e;
        BBoxDiagonalMm = bboxDiagonalMm ?? ComputeBBoxDiagonal(edges);
    }

    /// <summary>Whether an edge with the given id exists in this topology.</summary>
    public bool Has(string edgeId) => edgeId is not null && _byId.ContainsKey(edgeId);

    /// <summary>Gets the edge by id, or <c>null</c> if absent.</summary>
    public EdgeTopology? Get(string edgeId) =>
        edgeId is not null && _byId.TryGetValue(edgeId, out var e) ? e : null;

    private static double ComputeBBoxDiagonal(IReadOnlyList<EdgeTopology> edges)
    {
        var min = new Vector3<double>(double.MaxValue, double.MaxValue, double.MaxValue);
        var max = new Vector3<double>(double.MinValue, double.MinValue, double.MinValue);
        var any = false;
        foreach (var e in edges)
        {
            foreach (var p in e.Polyline)
            {
                any = true;
                min = new Vector3<double>(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                max = new Vector3<double>(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
            }
        }
        if (!any) return 1.0; // degenerate; avoid divide-by-zero downstream
        var diag = (max - min).Length;
        return diag <= 0 ? 1.0 : diag;
    }
}
