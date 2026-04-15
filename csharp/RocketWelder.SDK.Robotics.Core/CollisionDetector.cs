using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// Pure static collision queries: self-collision (capsule-capsule over non-adjacent link pairs)
/// plus environment collision delegated to <see cref="ICollisionSource"/>.
/// Thread-safe.
/// </summary>
public static class CollisionDetector
{
    /// <summary>
    /// Non-adjacent link pairs checked for self-collision. Adjacent pairs share a joint
    /// and would report spurious contacts at the shared origin, so they are excluded.
    /// Indices are zero-based (L1 = 0 ... L6 = 5).
    /// </summary>
    public static readonly IReadOnlyList<(int A, int B)> SelfCollisionPairs = new (int, int)[]
    {
        (0, 2), (0, 3), (0, 4), (0, 5),
        (1, 3), (1, 4), (1, 5),
        (2, 4), (2, 5),
        (3, 5),
    };

    /// <summary>
    /// Reports every self-collision pair and every environment collision for the given pose.
    /// Returns an empty list when no contact. Never returns null.
    /// </summary>
    public static IReadOnlyList<CollisionResult> CheckCollision(
        RobotModel model,
        Joints6<double> joints,
        CollisionEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(environment);

        var state = ForwardKinematics.Compute(model, joints);
        Span<Point3<double>> p0s = stackalloc Point3<double>[6];
        Span<Point3<double>> p1s = stackalloc Point3<double>[6];
        for (int i = 0; i < 6; i++)
        {
            p0s[i] = i == 0 ? new Point3<double>(0, 0, 0) : FramePosition(state, i - 1);
            p1s[i] = FramePosition(state, i);
        }

        var radii = environment.LinkRadii;
        var margin = environment.SafetyMargin;

        List<CollisionResult>? hits = null;

        for (int k = 0; k < SelfCollisionPairs.Count; k++)
        {
            var (a, b) = SelfCollisionPairs[k];
            var (pa, pb, dist) = CollisionMath.ClosestPointsOnSegments(
                p0s[a], p1s[a], p0s[b], p1s[b]);

            var clearance = dist - radii[a] - radii[b];
            if (clearance < margin)
            {
                hits ??= new List<CollisionResult>(4);
                var midpoint = new Point3<double>(
                    (pa.X + pb.X) * 0.5,
                    (pa.Y + pb.Y) * 0.5,
                    (pa.Z + pb.Z) * 0.5);
                hits.Add(new CollisionResult(
                    $"Link{a + 1}", $"Link{b + 1}", -clearance, midpoint));
            }
        }

        var envHits = environment.Source.QueryCollision(model, joints, radii, environment.Tool, margin);
        if (envHits.Count == 0)
            return hits ?? (IReadOnlyList<CollisionResult>)Array.Empty<CollisionResult>();

        if (hits is null) return envHits;

        for (int i = 0; i < envHits.Count; i++)
            hits.Add(envHits[i]);
        return hits;
    }

    private static Point3<double> FramePosition(RobotState state, int linkIndex)
    {
        var pose = state.FramePoses[linkIndex];
        return new Point3<double>(pose.X, pose.Y, pose.Z);
    }
}
