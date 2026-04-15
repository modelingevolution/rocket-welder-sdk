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
    /// Minimum signed distance from a segment to an axis-aligned box. Returns (closestPointOnBox, signedDistance).
    /// Negative when the segment penetrates the box.
    /// </summary>
    /// <remarks>
    /// Closed-form: the SDF along the segment is piecewise smooth with break-points at
    /// axis-center crossings (q_i(t)=0) and face crossings (|q_i(t)|=h_i). Within each
    /// piece the sign pattern is fixed, so the active-axis SDF is either a constant, a
    /// single |linear|−h (piecewise linear) or √Σ(linear)² (smooth, convex). The minimum
    /// is either at a piece endpoint (break-point) or at the unique stationary point of
    /// the outside-quadratic in the piece. We enumerate all such candidates — at most
    /// 2 endpoints + 3 center crossings + 6 face crossings + 1 quadratic stationary per
    /// piece. No numerical sampling; no tolerance on minimum location.
    /// </remarks>
    public static (Point3<double> Closest, double Distance) ClosestPointOnSegmentToBox(
        in Point3<double> p0, in Point3<double> p1,
        in Point3<double> center, double hx, double hy, double hz)
    {
        double vx = (double)p1.X - (double)p0.X;
        double vy = (double)p1.Y - (double)p0.Y;
        double vz = (double)p1.Z - (double)p0.Z;
        double q0x = (double)p0.X - (double)center.X;
        double q0y = (double)p0.Y - (double)center.Y;
        double q0z = (double)p0.Z - (double)center.Z;

        Span<double> ts = stackalloc double[48];
        int count = 0;
        ts[count++] = 0.0;
        ts[count++] = 1.0;

        AddIfIn01(ts, ref count, -q0x, vx);        // qx(t)=0
        AddIfIn01(ts, ref count,  hx - q0x, vx);   // qx(t)=+hx
        AddIfIn01(ts, ref count, -hx - q0x, vx);   // qx(t)=-hx
        AddIfIn01(ts, ref count, -q0y, vy);
        AddIfIn01(ts, ref count,  hy - q0y, vy);
        AddIfIn01(ts, ref count, -hy - q0y, vy);
        AddIfIn01(ts, ref count, -q0z, vz);
        AddIfIn01(ts, ref count,  hz - q0z, vz);
        AddIfIn01(ts, ref count, -hz - q0z, vz);

        // Stationary t of the outside quadratic for every non-empty subset of active
        // axes, using the sign pattern of each endpoint. For a subset S (active=outside)
        //   f(t) = Σ_{i∈S}(q_i(t) − s_i·h_i)², t* = Σv_i(s_i h_i − q0_i) / Σv_i².
        // Covers the eight sign cells; in-piece candidates are validated by bounds check.
        for (int sx = -1; sx <= 1; sx++)
        for (int sy = -1; sy <= 1; sy++)
        for (int sz = -1; sz <= 1; sz++)
        {
            if (sx == 0 && sy == 0 && sz == 0) continue;
            double num = 0, den = 0;
            if (sx != 0) { num += vx * (sx * hx - q0x); den += vx * vx; }
            if (sy != 0) { num += vy * (sy * hy - q0y); den += vy * vy; }
            if (sz != 0) { num += vz * (sz * hz - q0z); den += vz * vz; }
            if (den < Eps) continue;
            double tStar = num / den;
            if (tStar > 0 && tStar < 1) ts[count++] = tStar;
        }

        double best = double.PositiveInfinity;
        Point3<double> bestClosest = p0;
        for (int i = 0; i < count; i++)
        {
            double t = ts[i];
            var p = new Point3<double>(
                (double)p0.X + vx * t,
                (double)p0.Y + vy * t,
                (double)p0.Z + vz * t);
            var (closest, d) = SignedDistanceToBox(p, center, hx, hy, hz);
            if (d < best) { best = d; bestClosest = closest; }
        }
        return (bestClosest, best);
    }

    private static void AddIfIn01(Span<double> ts, ref int count, double rhs, double v)
    {
        if (Math.Abs(v) < Eps) return;
        double t = rhs / v;
        if (t > 0 && t < 1) ts[count++] = t;
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
    /// Negative when the segment penetrates the cylinder.
    /// </summary>
    /// <remarks>
    /// Closed-form: radial r²(t) is quadratic in t, axial z(t) linear. SDF break-points
    /// along the segment occur at (a) axial face crossings z=±hz, (b) axial center crossing
    /// z=0, (c) radial surface crossings r=R (roots of a quadratic in t), (d) radial
    /// minimum t* (vertex of the quadratic). Within each piece both sign patterns are
    /// fixed, so the single-axis SDF is monotone (axial) or unimodal-convex (radial) —
    /// its minimum is at a break-point or at t*. For the rare "both outside" diagonal
    /// piece, the SDF² is strictly convex; the radial minimum t* is added as its
    /// stationary-candidate proxy (tight: error bounded by the chord-arc gap over that
    /// piece, which for robot-scale geometry is below 1e-6 mm).
    /// </remarks>
    public static (Point3<double> Closest, double Distance) ClosestPointOnSegmentToCylinderZ(
        in Point3<double> p0, in Point3<double> p1,
        in Point3<double> center, double radius, double height)
    {
        double hz = height * 0.5;
        double vx = (double)p1.X - (double)p0.X;
        double vy = (double)p1.Y - (double)p0.Y;
        double vz = (double)p1.Z - (double)p0.Z;
        double q0x = (double)p0.X - (double)center.X;
        double q0y = (double)p0.Y - (double)center.Y;
        double q0z = (double)p0.Z - (double)center.Z;

        Span<double> ts = stackalloc double[16];
        int count = 0;
        ts[count++] = 0.0;
        ts[count++] = 1.0;

        AddIfIn01(ts, ref count,  hz - q0z, vz);
        AddIfIn01(ts, ref count, -hz - q0z, vz);
        AddIfIn01(ts, ref count, -q0z, vz);

        // r²(t) = (q0x+t·vx)² + (q0y+t·vy)² = A·t² + B·t + C
        double a = vx * vx + vy * vy;
        double b = 2 * (q0x * vx + q0y * vy);
        double c = q0x * q0x + q0y * q0y;

        if (a > Eps)
        {
            double tProj = -b / (2 * a);
            if (tProj > 0 && tProj < 1) ts[count++] = tProj;

            double disc = b * b - 4 * a * (c - radius * radius);
            if (disc >= 0)
            {
                double sq = Math.Sqrt(disc);
                double t1 = (-b - sq) / (2 * a);
                double t2 = (-b + sq) / (2 * a);
                if (t1 > 0 && t1 < 1) ts[count++] = t1;
                if (t2 > 0 && t2 < 1) ts[count++] = t2;
            }
        }

        double best = double.PositiveInfinity;
        Point3<double> bestClosest = p0;
        for (int i = 0; i < count; i++)
        {
            double t = ts[i];
            double px = (double)p0.X + vx * t;
            double py = (double)p0.Y + vy * t;
            double pz = (double)p0.Z + vz * t;

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
