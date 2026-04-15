using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// Immutable environment primitive. Provides a closed-form distance from a line segment
/// (the swept axis of a robot link or tool capsule) to this primitive's surface.
/// </summary>
public abstract record CollisionPrimitive(string Name)
{
    /// <summary>
    /// Minimum signed distance from the segment <c>[p0, p1]</c> to this primitive's surface, in mm.
    /// Negative when the segment penetrates the primitive.
    /// </summary>
    public abstract double SignedDistanceToSegment(in Point3<double> p0, in Point3<double> p1);

    /// <summary>
    /// Returns the closest point on the primitive surface to the given segment, used as a contact-point witness.
    /// </summary>
    public abstract Point3<double> ClosestPointToSegment(in Point3<double> p0, in Point3<double> p1);
}

/// <summary>Axis-aligned box centred at <see cref="Center"/> with half-extents <see cref="HalfSizeX"/>, <see cref="HalfSizeY"/>, <see cref="HalfSizeZ"/> (mm).</summary>
public sealed record BoxPrimitive(string Name, Point3<double> Center,
    double HalfSizeX, double HalfSizeY, double HalfSizeZ) : CollisionPrimitive(Name)
{
    /// <inheritdoc />
    public override double SignedDistanceToSegment(in Point3<double> p0, in Point3<double> p1)
    {
        var (closest, d) = CollisionMath.ClosestPointOnSegmentToBox(p0, p1, Center, HalfSizeX, HalfSizeY, HalfSizeZ);
        _ = closest;
        return d;
    }

    /// <inheritdoc />
    public override Point3<double> ClosestPointToSegment(in Point3<double> p0, in Point3<double> p1)
    {
        var (closest, _) = CollisionMath.ClosestPointOnSegmentToBox(p0, p1, Center, HalfSizeX, HalfSizeY, HalfSizeZ);
        return closest;
    }
}

/// <summary>Sphere of the given <see cref="Radius"/> (mm) at <see cref="Center"/>.</summary>
public sealed record SpherePrimitive(string Name, Point3<double> Center, double Radius) : CollisionPrimitive(Name)
{
    /// <inheritdoc />
    public override double SignedDistanceToSegment(in Point3<double> p0, in Point3<double> p1)
    {
        var (_, d) = CollisionMath.ClosestPointOnSegment(p0, p1, Center);
        return d - Radius;
    }

    /// <inheritdoc />
    public override Point3<double> ClosestPointToSegment(in Point3<double> p0, in Point3<double> p1)
    {
        var (closest, _) = CollisionMath.ClosestPointOnSegment(p0, p1, Center);
        return closest;
    }
}

/// <summary>Capsule between <see cref="A"/> and <see cref="B"/> with <see cref="Radius"/> (mm).</summary>
public sealed record CapsulePrimitive(string Name, Point3<double> A, Point3<double> B, double Radius) : CollisionPrimitive(Name)
{
    /// <inheritdoc />
    public override double SignedDistanceToSegment(in Point3<double> p0, in Point3<double> p1)
    {
        var (_, _, d) = CollisionMath.ClosestPointsOnSegments(p0, p1, A, B);
        return d - Radius;
    }

    /// <inheritdoc />
    public override Point3<double> ClosestPointToSegment(in Point3<double> p0, in Point3<double> p1)
    {
        var (_, q, _) = CollisionMath.ClosestPointsOnSegments(p0, p1, A, B);
        return q;
    }
}

/// <summary>
/// Cylinder aligned with world Z, centered at (<see cref="Center"/>) with the given <see cref="Radius"/> and total <see cref="Height"/> (mm).
/// </summary>
public sealed record CylinderPrimitive(string Name, Point3<double> Center, double Radius, double Height) : CollisionPrimitive(Name)
{
    /// <inheritdoc />
    public override double SignedDistanceToSegment(in Point3<double> p0, in Point3<double> p1)
    {
        var (_, d) = CollisionMath.ClosestPointOnSegmentToCylinderZ(p0, p1, Center, Radius, Height);
        return d;
    }

    /// <inheritdoc />
    public override Point3<double> ClosestPointToSegment(in Point3<double> p0, in Point3<double> p1)
    {
        var (closest, _) = CollisionMath.ClosestPointOnSegmentToCylinderZ(p0, p1, Center, Radius, Height);
        return closest;
    }
}

/// <summary>
/// Infinite plane defined by a point on the plane (<see cref="PointOnPlane"/>) and a unit <see cref="Normal"/>.
/// Positive side of the plane is where the normal points.
/// </summary>
public sealed record PlanePrimitive(string Name, Point3<double> PointOnPlane, Vector3<double> Normal) : CollisionPrimitive(Name)
{
    /// <inheritdoc />
    public override double SignedDistanceToSegment(in Point3<double> p0, in Point3<double> p1)
    {
        var d0 = CollisionMath.SignedDistanceToPlane(p0, PointOnPlane, Normal);
        var d1 = CollisionMath.SignedDistanceToPlane(p1, PointOnPlane, Normal);
        return Math.Min(d0, d1);
    }

    /// <inheritdoc />
    public override Point3<double> ClosestPointToSegment(in Point3<double> p0, in Point3<double> p1)
    {
        var d0 = CollisionMath.SignedDistanceToPlane(p0, PointOnPlane, Normal);
        var d1 = CollisionMath.SignedDistanceToPlane(p1, PointOnPlane, Normal);
        var closer = d0 <= d1 ? p0 : p1;
        return CollisionMath.ProjectOntoPlane(closer, PointOnPlane, Normal);
    }
}
