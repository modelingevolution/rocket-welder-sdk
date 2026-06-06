using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Operations;

/// <summary>
/// Computes durable edge fingerprints and re-resolves an <see cref="EdgeBinding"/> against a live
/// topology (per <c>data-model.md</c> §3.2 / §3.3). Pure geometry — no welding, no Blazor, headless.
/// Lives in the SDK per ADR-002.
/// </summary>
public sealed class EdgeBindingResolver
{
    /// <summary>Binding tolerance (dimensionless, after normalization). Default per §3.3.</summary>
    public const double TolBind = 0.05;

    /// <summary>Disambiguation margin between the best and second-best candidate. Default = 0.5·TOL_BIND (§3.3).</summary>
    public const double Margin = 0.5 * TolBind;

    // Distance-metric weights (§3.3 defaults).
    private const double WLength = 1.0;
    private const double WMidpoint = 1.0;
    private const double WTangent = 0.5;
    private const double WNormals = 0.5;

    private readonly double _tolBind;
    private readonly double _margin;

    /// <summary>Creates a resolver with the default §3.3 tolerances.</summary>
    public EdgeBindingResolver() : this(TolBind, Margin) { }

    /// <summary>Creates a resolver with explicit tolerances (for AT-E2 tuning harness).</summary>
    public EdgeBindingResolver(double tolBind, double margin)
    {
        _tolBind = tolBind;
        _margin = margin;
    }

    /// <summary>
    /// Builds the canonical durable fingerprint for an edge in the live topology (§3.1/§3.3):
    /// midpoint at t=0.5, length, sign-normalized tangent, lexicographically-sorted endpoints,
    /// canonically-sorted adjacent-face normals.
    /// </summary>
    public EdgeBinding Fingerprint(EdgeTopology edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        var poly = edge.Polyline;
        if (poly.Count < 2)
            throw new ArgumentException($"Edge '{edge.EdgeId}' polyline must have >= 2 points.", nameof(edge));

        var start = poly[0];
        var end = poly[^1];

        // Endpoints sorted lexicographically by (x,y,z).
        Vector3<double> e0, e1;
        if (LexCompare(start, end) <= 0) { e0 = start; e1 = end; }
        else { e0 = end; e1 = start; }

        var midpoint = MidpointOf(poly);
        var rawTangent = TangentAtMid(poly);

        // Tangent oriented endpoint[0] -> endpoint[1].
        var orient = (e1 - e0);
        var tangent = NormalizeOrZero(rawTangent);
        if (Vector3<double>.Dot(tangent, orient) < 0)
            tangent = -tangent;

        var normals = CanonicalSortNormals(edge.AdjacentFaceNormals);

        return new EdgeBinding(
            EdgeIdHint: edge.EdgeId,
            Kind: edge.Kind,
            LengthMm: edge.Length,
            Midpoint: midpoint,
            TangentAtMid: tangent,
            Endpoints: new[] { e0, e1 },
            AdjFaceNormals: normals);
    }

    /// <summary>
    /// Weighted, scale-normalized distance between an edge (via its fingerprint) and a stored
    /// binding (§3.3). Smaller is closer; 0 is identical.
    /// </summary>
    public double Dist(EdgeTopology edge, EdgeBinding binding, double bboxDiagonalMm) =>
        Dist(Fingerprint(edge), binding, bboxDiagonalMm);

    /// <summary>
    /// Weighted, scale-normalized distance between a (canonical) candidate fingerprint and a stored
    /// binding (§3.3).
    /// </summary>
    public double Dist(EdgeBinding candidate, EdgeBinding binding, double bboxDiagonalMm)
    {
        var lenB = binding.LengthMm;
        var relLen = lenB > 0
            ? Math.Abs(candidate.LengthMm - lenB) / lenB
            : Math.Abs(candidate.LengthMm - lenB);

        var diag = bboxDiagonalMm > 0 ? bboxDiagonalMm : 1.0;
        var midTerm = (candidate.Midpoint - binding.Midpoint).Length / diag;

        // tangent direction: 0 when parallel (sign-insensitive via |t_e·t_b|).
        var tanTerm = 1.0 - Math.Abs(Vector3<double>.Dot(candidate.TangentAtMid, binding.TangentAtMid));

        var normTerm = MeanNormalMismatch(candidate.AdjFaceNormals, binding.AdjFaceNormals);

        return WLength * relLen
             + WMidpoint * midTerm
             + WTangent * tanTerm
             + WNormals * normTerm;
    }

    /// <summary>
    /// Re-resolves a stored binding against the live topology (§3.2). Returns the matched edge id,
    /// or <see cref="Unresolved"/> when ambiguous or no candidate is within tolerance — it NEVER
    /// silently picks the best-but-uncertain candidate.
    /// </summary>
    public string Resolve(EdgeBinding binding, Topology topology)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(topology);
        var diag = topology.BBoxDiagonalMm;

        // Fast path: trusted-only-if-it-matches hint (same-STEP reload).
        if (topology.Has(binding.EdgeIdHint))
        {
            var hinted = topology.Get(binding.EdgeIdHint)!;
            if (hinted.Kind == binding.Kind &&
                Dist(Fingerprint(hinted), binding, diag) <= _tolBind)
            {
                return hinted.EdgeId;
            }
        }

        // General path: nearest fingerprint over all edges of the same kind.
        var scored = new List<(string edgeId, double dist)>();
        foreach (var edge in topology.Edges)
        {
            if (edge.Kind != binding.Kind) continue;
            scored.Add((edge.EdgeId, Dist(Fingerprint(edge), binding, diag)));
        }

        if (scored.Count == 0)
            return Unresolved;

        scored.Sort(static (a, b) => a.dist.CompareTo(b.dist));
        var best = scored[0];
        var hasSecond = scored.Count > 1;
        var second = hasSecond ? scored[1] : default;

        if (best.dist <= _tolBind && (!hasSecond || (second.dist - best.dist) >= _margin))
            return best.edgeId;

        return Unresolved;
    }

    /// <summary>Sentinel returned by <see cref="Resolve"/> when no unambiguous edge is found.</summary>
    public const string Unresolved = "UNRESOLVED";

    // --- geometry helpers ------------------------------------------------

    private static Vector3<double> MidpointOf(IReadOnlyList<Vector3<double>> poly)
    {
        // Arclength midpoint (t=0.5): walk the polyline to half the total length.
        var total = 0.0;
        for (var i = 1; i < poly.Count; i++)
            total += (poly[i] - poly[i - 1]).Length;

        if (total <= 0) return poly[0];
        var half = total / 2.0;

        var acc = 0.0;
        for (var i = 1; i < poly.Count; i++)
        {
            var seg = (poly[i] - poly[i - 1]).Length;
            if (acc + seg >= half)
            {
                var t = seg > 0 ? (half - acc) / seg : 0.0;
                return Lerp(poly[i - 1], poly[i], t);
            }
            acc += seg;
        }
        return poly[^1];
    }

    private static Vector3<double> TangentAtMid(IReadOnlyList<Vector3<double>> poly)
    {
        // Direction of the polyline segment that contains the arclength midpoint.
        var total = 0.0;
        for (var i = 1; i < poly.Count; i++)
            total += (poly[i] - poly[i - 1]).Length;

        if (total <= 0) return Vector3<double>.EX;
        var half = total / 2.0;

        var acc = 0.0;
        for (var i = 1; i < poly.Count; i++)
        {
            var d = poly[i] - poly[i - 1];
            var seg = d.Length;
            if (acc + seg >= half)
                return NormalizeOrZero(d);
            acc += seg;
        }
        return NormalizeOrZero(poly[^1] - poly[^2]);
    }

    private static IReadOnlyList<Vector3<double>> CanonicalSortNormals(IReadOnlyList<Vector3<double>> normals)
    {
        var copy = new List<Vector3<double>>(normals.Count);
        foreach (var n in normals)
            copy.Add(NormalizeOrZero(n));
        copy.Sort(LexCompare);
        return copy;
    }

    private static double MeanNormalMismatch(
        IReadOnlyList<Vector3<double>> a,
        IReadOnlyList<Vector3<double>> b)
    {
        // Both lists are canonically sorted. Compare pairwise over the shared count;
        // a boundary edge (1 normal) vs an interior edge (2) compares the entries that exist.
        var n = Math.Min(a.Count, b.Count);
        if (n == 0) return 0.0;
        var sum = 0.0;
        for (var i = 0; i < n; i++)
            sum += 1.0 - Vector3<double>.Dot(a[i], b[i]);
        return sum / n;
    }

    private static Vector3<double> Lerp(Vector3<double> a, Vector3<double> b, double t) =>
        a + (b - a) * t;

    private static Vector3<double> NormalizeOrZero(Vector3<double> v)
    {
        var len = v.Length;
        return len > 0 ? v / len : v;
    }

    private static int LexCompare(Vector3<double> a, Vector3<double> b)
    {
        var c = a.X.CompareTo(b.X);
        if (c != 0) return c;
        c = a.Y.CompareTo(b.Y);
        if (c != 0) return c;
        return a.Z.CompareTo(b.Z);
    }
}
