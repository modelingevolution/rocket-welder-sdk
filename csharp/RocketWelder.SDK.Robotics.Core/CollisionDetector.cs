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
    /// Returns an empty array when no contact. Never returns null.
    /// </summary>
    public static CollisionResult[] CheckCollision(
        RobotModel model,
        Joints6<double> joints,
        CollisionEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(environment);

        var state = ForwardKinematics.Compute(model, joints);
        var segments = new (Point3<double> P0, Point3<double> P1)[6];
        for (int i = 0; i < 6; i++)
        {
            var p0 = i == 0 ? new Point3<double>(0, 0, 0) : FramePosition(state, i - 1);
            var p1 = FramePosition(state, i);
            segments[i] = (p0, p1);
        }

        var radii = environment.LinkRadii;
        var margin = environment.SafetyMargin;

        List<CollisionResult>? hits = null;

        foreach (var (a, b) in SelfCollisionPairs)
        {
            var (pa, pb, dist) = CollisionMath.ClosestPointsOnSegments(
                segments[a].P0, segments[a].P1,
                segments[b].P0, segments[b].P1);

            var clearance = dist - radii[a] - radii[b];
            if (clearance < margin)
            {
                hits ??= new List<CollisionResult>();
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
            return hits is null ? Array.Empty<CollisionResult>() : hits.ToArray();

        hits ??= new List<CollisionResult>(envHits.Count);
        hits.AddRange(envHits);
        return hits.ToArray();
    }

    private static Point3<double> FramePosition(RobotState state, int linkIndex)
    {
        var pose = state.FramePoses[linkIndex];
        return new Point3<double>(pose.X, pose.Y, pose.Z);
    }
}
