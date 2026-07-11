using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// Immutable <see cref="ICollisionSource"/> backed by a fixed list of <see cref="CollisionPrimitive"/>s.
/// Thread-safe; one instance can be shared across simulators.
/// </summary>
public sealed class PrimitiveCollisionSource : ICollisionSource
{
    private readonly IReadOnlyList<CollisionPrimitive> _primitives;

    /// <summary>Creates a source backed by the given primitives. The list is captured by reference; callers must not mutate it.</summary>
    public PrimitiveCollisionSource(IReadOnlyList<CollisionPrimitive> primitives)
    {
        ArgumentNullException.ThrowIfNull(primitives);
        _primitives = primitives;
    }

    /// <summary>Convenience: create from a params array.</summary>
    public PrimitiveCollisionSource(params CollisionPrimitive[] primitives)
        : this((IReadOnlyList<CollisionPrimitive>)primitives) { }

    /// <summary>The underlying primitive list.</summary>
    public IReadOnlyList<CollisionPrimitive> Primitives => _primitives;

    /// <inheritdoc />
    public IReadOnlyList<CollisionResult> QueryCollision(
        RobotModel model,
        Joints6<double> joints,
        IReadOnlyList<double> linkRadii,
        ToolModel tool,
        double safetyMargin)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(linkRadii);
        ArgumentNullException.ThrowIfNull(tool);
        if (linkRadii.Count != 6)
            throw new ArgumentException("Link radii must have exactly 6 entries.", nameof(linkRadii));
        if (safetyMargin < 0)
            throw new ArgumentOutOfRangeException(nameof(safetyMargin), "Safety margin must be non-negative.");

        var state = ForwardKinematics.Compute(model, joints);
        var results = new List<CollisionResult>();

        // Link capsules: segment between consecutive frame origins, with configured radius.
        for (int link = 0; link < 6; link++)
        {
            var p0 = link == 0 ? new Point3<double>(0, 0, 0) : FramePosition(state, link - 1);
            var p1 = FramePosition(state, link);
            double r = linkRadii[link];
            var linkId = $"Link{link + 1}";

            foreach (var prim in _primitives)
            {
                var sdf = prim.SignedDistanceToSegment(p0, p1);
                var clearance = sdf - r;
                if (clearance < safetyMargin)
                {
                    var contact = prim.ClosestPointToSegment(p0, p1);
                    results.Add(new CollisionResult(linkId, prim.Name, -clearance, contact));
                }
            }
        }

        // Tool capsule: from flange origin along the flange +Z axis (only when a CapsuleToolModel is attached).
        if (tool is CapsuleToolModel capsule)
        {
            var flangePos = FramePosition(state, 5);
            var tipPos = ExtrudeAlongFlangeZ(state, capsule.Length);
            foreach (var prim in _primitives)
            {
                var sdf = prim.SignedDistanceToSegment(flangePos, tipPos);
                var clearance = sdf - capsule.Radius;
                if (clearance < safetyMargin)
                {
                    var contact = prim.ClosestPointToSegment(flangePos, tipPos);
                    results.Add(new CollisionResult("Tool", prim.Name, -clearance, contact));
                }
            }
        }

        return results;
    }

    private static Point3<double> FramePosition(RobotState state, int linkIndex)
    {
        var pose = state.FramePoses[linkIndex];
        return new Point3<double>(pose.X, pose.Y, pose.Z);
    }

    private static Point3<double> ExtrudeAlongFlangeZ(RobotState state, double length)
    {
        // Use frame 6 (flange) pose; rotate +Z by its ZYX euler to get the flange Z axis direction in base frame.
        var flange = state.FramePoses[5];
        double rx = (double)flange.Rx * Math.PI / 180.0;
        double ry = (double)flange.Ry * Math.PI / 180.0;
        double rz = (double)flange.Rz * Math.PI / 180.0;
        double cx = Math.Cos(rx), sx = Math.Sin(rx);
        double cy = Math.Cos(ry), sy = Math.Sin(ry);
        double cz = Math.Cos(rz), sz = Math.Sin(rz);

        // R * (0,0,1) column = (cz*sy*cx + sz*sx, sz*sy*cx - cz*sx, cy*cx)
        double zx = cz * sy * cx + sz * sx;
        double zy = sz * sy * cx - cz * sx;
        double zz = cy * cx;

        return new Point3<double>(
            (double)flange.X + zx * length,
            (double)flange.Y + zy * length,
            (double)flange.Z + zz * length);
    }
}
