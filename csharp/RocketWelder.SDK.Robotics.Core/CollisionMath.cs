using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// Closed-form helpers for capsule vs primitive distance queries.
/// All units are millimetres. Thread-safe (pure static).
/// </summary>
internal static class CollisionMath
{
    private const double Eps = 1e-10;

    /// <summary>
    /// Returns (closestPointOnSegment, distanceFromPoint).
    /// </summary>
    public static (Point3<double> Closest, double Distance) ClosestPointOnSegment(
        in Point3<double> a, in Point3<double> b, in Point3<double> p)
    {
        double abx = (double)b.X - (double)a.X;
        double aby = (double)b.Y - (double)a.Y;
        double abz = (double)b.Z - (double)a.Z;

        double apx = (double)p.X - (double)a.X;
        double apy = (double)p.Y - (double)a.Y;
        double apz = (double)p.Z - (double)a.Z;

        double denom = abx * abx + aby * aby + abz * abz;
        double t = denom < Eps ? 0.0 : Math.Clamp((apx * abx + apy * aby + apz * abz) / denom, 0.0, 1.0);

        double cx = (double)a.X + abx * t;
        double cy = (double)a.Y + aby * t;
        double cz = (double)a.Z + abz * t;

        double dx = (double)p.X - cx;
        double dy = (double)p.Y - cy;
        double dz = (double)p.Z - cz;

        return (new Point3<double>(cx, cy, cz), Math.Sqrt(dx * dx + dy * dy + dz * dz));
    }

    /// <summary>
    /// Returns the closest points on two line segments and the minimum distance between them.
    /// </summary>
    public static (Point3<double> ClosestA, Point3<double> ClosestB, double Distance) ClosestPointsOnSegments(
        in Point3<double> p1, in Point3<double> p2, in Point3<double> p3, in Point3<double> p4)
    {
        double d1x = (double)p2.X - (double)p1.X, d1y = (double)p2.Y - (double)p1.Y, d1z = (double)p2.Z - (double)p1.Z;
        double d2x = (double)p4.X - (double)p3.X, d2y = (double)p4.Y - (double)p3.Y, d2z = (double)p4.Z - (double)p3.Z;
        double rx = (double)p1.X - (double)p3.X, ry = (double)p1.Y - (double)p3.Y, rz = (double)p1.Z - (double)p3.Z;

        double a = d1x * d1x + d1y * d1y + d1z * d1z;
        double e = d2x * d2x + d2y * d2y + d2z * d2z;
        double f = d2x * rx + d2y * ry + d2z * rz;

        double s, t;
        if (a <= Eps && e <= Eps)
        {
            s = t = 0;
        }
        else if (a <= Eps)
        {
            s = 0;
            t = Math.Clamp(f / e, 0, 1);
        }
        else
        {
            double c = d1x * rx + d1y * ry + d1z * rz;
            if (e <= Eps)
            {
                t = 0;
                s = Math.Clamp(-c / a, 0, 1);
            }
            else
            {
                double b = d1x * d2x + d1y * d2y + d1z * d2z;
                double denom = a * e - b * b;
                s = denom != 0 ? Math.Clamp((b * f - c * e) / denom, 0, 1) : 0;
                t = (b * s + f) / e;
                if (t < 0) { t = 0; s = Math.Clamp(-c / a, 0, 1); }
                else if (t > 1) { t = 1; s = Math.Clamp((b - c) / a, 0, 1); }
            }
        }

        var cA = new Point3<double>((double)p1.X + d1x * s, (double)p1.Y + d1y * s, (double)p1.Z + d1z * s);
        var cB = new Point3<double>((double)p3.X + d2x * t, (double)p3.Y + d2y * t, (double)p3.Z + d2z * t);

        double dx = (double)cA.X - (double)cB.X;
        double dy = (double)cA.Y - (double)cB.Y;
        double dz = (double)cA.Z - (double)cB.Z;

        return (cA, cB, Math.Sqrt(dx * dx + dy * dy + dz * dz));
    }

    /// <summary>
    /// Minimum distance from a segment to an axis-aligned box. Returns (closestPointOnBox, signedDistance).
    /// Signed distance is negative when the segment penetrates the box.
    /// </summary>
    public static (Point3<double> Closest, double Distance) ClosestPointOnSegmentToBox(
        in Point3<double> p0, in Point3<double> p1,
        in Point3<double> center, double hx, double hy, double hz)
    {
        // Evaluate signed distance at both endpoints and the projection of each axis bound.
        // For convex box/segment, the minimum of sdf(p(t)) is attained at an endpoint or
        // at a point where ∂/∂t ‖clamp(p(t)-c, ±h) - (p(t)-c)‖ changes — approximated by sampling.
        double best = double.PositiveInfinity;
        Point3<double> bestClosest = p0;
        for (int i = 0; i <= 16; i++)
        {
            double t = i / 16.0;
            var p = new Point3<double>(
                (double)p0.X + ((double)p1.X - (double)p0.X) * t,
                (double)p0.Y + ((double)p1.Y - (double)p0.Y) * t,
                (double)p0.Z + ((double)p1.Z - (double)p0.Z) * t);
            var (closest, d) = SignedDistanceToBox(p, center, hx, hy, hz);
            if (d < best)
            {
                best = d;
                bestClosest = closest;
            }
        }
        return (bestClosest, best);
    }

    /// <summary>
    /// Signed distance from a point to an axis-aligned box. Returns closest surface point and signed distance.
    /// </summary>
    public static (Point3<double> Closest, double Distance) SignedDistanceToBox(
        in Point3<double> p, in Point3<double> center, double hx, double hy, double hz)
    {
        double dx = (double)p.X - (double)center.X;
        double dy = (double)p.Y - (double)center.Y;
        double dz = (double)p.Z - (double)center.Z;

        double qx = Math.Abs(dx) - hx;
        double qy = Math.Abs(dy) - hy;
        double qz = Math.Abs(dz) - hz;

        double outside = Math.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) +
                                    Math.Max(qy, 0) * Math.Max(qy, 0) +
                                    Math.Max(qz, 0) * Math.Max(qz, 0));
        double inside = Math.Min(Math.Max(qx, Math.Max(qy, qz)), 0);
        double sdf = outside + inside;

        double cx = (double)center.X + Math.Clamp(dx, -hx, hx);
        double cy = (double)center.Y + Math.Clamp(dy, -hy, hy);
        double cz = (double)center.Z + Math.Clamp(dz, -hz, hz);

        return (new Point3<double>(cx, cy, cz), sdf);
    }

    /// <summary>
    /// Closest-point and signed distance from a segment to an axis-aligned cylinder aligned with world Z.
    /// </summary>
    public static (Point3<double> Closest, double Distance) ClosestPointOnSegmentToCylinderZ(
        in Point3<double> p0, in Point3<double> p1,
        in Point3<double> center, double radius, double height)
    {
        double hz = height * 0.5;
        double best = double.PositiveInfinity;
        Point3<double> bestClosest = p0;
        for (int i = 0; i <= 16; i++)
        {
            double t = i / 16.0;
            double px = (double)p0.X + ((double)p1.X - (double)p0.X) * t;
            double py = (double)p0.Y + ((double)p1.Y - (double)p0.Y) * t;
            double pz = (double)p0.Z + ((double)p1.Z - (double)p0.Z) * t;

            double dx = px - (double)center.X;
            double dy = py - (double)center.Y;
            double dz = pz - (double)center.Z;

            double r = Math.Sqrt(dx * dx + dy * dy);
            double radial = r - radius;
            double axial = Math.Abs(dz) - hz;

            double sdf;
            if (radial > 0 && axial > 0)
                sdf = Math.Sqrt(radial * radial + axial * axial);
            else
                sdf = Math.Max(radial, axial);

            if (sdf < best)
            {
                best = sdf;
                double ratio = r > Eps ? radius / r : 0;
                double cx = (double)center.X + dx * ratio;
                double cy = (double)center.Y + dy * ratio;
                double cz = (double)center.Z + Math.Clamp(dz, -hz, hz);
                bestClosest = new Point3<double>(cx, cy, cz);
            }
        }
        return (bestClosest, best);
    }

    /// <summary>
    /// Signed distance from a point to an oriented plane defined by a point + normal.
    /// </summary>
    public static double SignedDistanceToPlane(in Point3<double> p, in Point3<double> pointOnPlane, in Vector3<double> normal)
    {
        double nx = (double)normal.X, ny = (double)normal.Y, nz = (double)normal.Z;
        double norm = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (norm < Eps) return double.PositiveInfinity;
        nx /= norm; ny /= norm; nz /= norm;
        return nx * ((double)p.X - (double)pointOnPlane.X)
             + ny * ((double)p.Y - (double)pointOnPlane.Y)
             + nz * ((double)p.Z - (double)pointOnPlane.Z);
    }

    /// <summary>
    /// Orthogonal projection of a point onto a plane.
    /// </summary>
    public static Point3<double> ProjectOntoPlane(in Point3<double> p, in Point3<double> pointOnPlane, in Vector3<double> normal)
    {
        double nx = (double)normal.X, ny = (double)normal.Y, nz = (double)normal.Z;
        double norm = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (norm < Eps) return p;
        nx /= norm; ny /= norm; nz /= norm;
        double d = SignedDistanceToPlane(p, pointOnPlane, normal);
        return new Point3<double>((double)p.X - nx * d, (double)p.Y - ny * d, (double)p.Z - nz * d);
    }
}
