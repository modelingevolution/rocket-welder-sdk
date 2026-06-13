using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Devices.Robot;

/// <summary>
/// Motion helpers on <see cref="IRobot"/> that expand to the shipped <c>MoveLin</c>/<c>MoveCircular</c>
/// primitives.
/// </summary>
public static class RobotMotionExtensions
{
    private const double Epsilon = 1e-9;
    private const double GateTolerance = 1e-12;
    private const double RadiusFitTolerance = 1e-6;

    // û·ŵ above this (corner bends by less than 2°) is treated as straight — passed straight through with
    // no arc. û and ŵ point away from the vertex, so a straight path has û·ŵ ≈ -1; a bend reduces it.
    private static readonly double StraightThroughCos = Math.Cos(178.0 * Math.PI / 180.0);

    /// <summary>
    /// Rounds the corner at <paramref name="vertex"/> with a fillet of <paramref name="radiusMm"/> and
    /// drives it: a straight run to the entry tangent at <paramref name="travel"/>, then the fillet arc to
    /// the exit tangent at <paramref name="turn"/>. The robot ends on the <paramref name="vertex"/>→
    /// <paramref name="next"/> edge at the exit tangent — the following move completes the run to
    /// <paramref name="next"/>. A non-rounding corner (radius ≤ 0, sharp, or near-collinear) drives a single
    /// straight move to <paramref name="vertex"/> instead. Synchronous, matching <see cref="IRobot.MoveLin"/>.
    /// </summary>
    /// <param name="robot">The robot to drive.</param>
    /// <param name="prev">Pose the robot is travelling from (the incoming edge's far end).</param>
    /// <param name="vertex">The corner pose being rounded.</param>
    /// <param name="next">Pose on the outgoing edge, giving the corner's exit direction.</param>
    /// <param name="radiusMm">Fillet radius in millimetres.</param>
    /// <param name="turn">Velocity for the fillet arc.</param>
    /// <param name="travel">Velocity for the straight approach.</param>
    /// <exception cref="ArgumentException">An adjoining edge has zero length.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radiusMm"/> does not fit on an
    /// adjoining edge.</exception>
    public static void MoveCorner(this IRobot robot, Pose3<double> prev, Pose3<double> vertex,
        Pose3<double> next, double radiusMm, Velocity turn, Velocity travel)
    {
        ArgumentNullException.ThrowIfNull(robot);

        var fillet = ComputeFillet(prev, vertex, next, radiusMm);
        if (fillet is not { } f)
        {
            robot.MoveLin(vertex, travel);
            return;
        }

        robot.MoveLin(f.Entry, travel);
        robot.MoveCircular(f.Via, f.Exit, turn);
    }

    /// <summary>
    /// Pure single-corner fillet over <see cref="ModelingEvolution.Drawing"/> primitives, matching Feature
    /// 001's <c>RoundedSeamPlanner</c> geometry. Returns the entry tangent, arc via, and exit tangent, or
    /// null when the corner does not round (radius ≤ 0, sharp, or near-collinear) and must pass straight
    /// through the vertex.
    /// </summary>
    private static (Pose3<double> Entry, Pose3<double> Via, Pose3<double> Exit)? ComputeFillet(
        Pose3<double> prev, Pose3<double> vertex, Pose3<double> next, double radiusMm)
    {
        var uVec = prev.Position - vertex.Position;
        var wVec = next.Position - vertex.Position;
        var edgeIn = uVec.Length;
        var edgeOut = wVec.Length;
        if (edgeIn < Epsilon || edgeOut < Epsilon)
            throw new ArgumentException("A corner edge has zero length (coincident poses).");

        var uHat = uVec / edgeIn;
        var wHat = wVec / edgeOut;
        var dot = Math.Clamp(Vector3<double>.Dot(uHat, wHat), -1.0, 1.0);

        // Sharp, zero-radius, or near-collinear corners don't round.
        if (!(radiusMm > 0 && dot > StraightThroughCos + GateTolerance))
            return null;

        var halfAngle = Math.Acos(dot) / 2.0;
        var setback = radiusMm / Math.Tan(halfAngle);

        // Single-corner fit (neighbours' setback demand is unknown to the SDK, so taken as zero): the radius
        // must not claim more than either adjoining edge can give.
        var maxFitRadius = Math.Min(edgeIn, edgeOut) * Math.Tan(halfAngle);
        if (radiusMm > maxFitRadius + RadiusFitTolerance)
            throw new ArgumentOutOfRangeException(nameof(radiusMm),
                $"Radius {radiusMm:0.###} mm does not fit; largest that fits here is {maxFitRadius:0.###} mm.");

        var entryTangent = vertex.Position + setback * uHat;
        var exitTangent = vertex.Position + setback * wHat;

        var bisector = (uHat + wHat).Normalize();
        var centre = vertex.Position + (radiusMm / Math.Sin(halfAngle)) * bisector;
        var via = centre - radiusMm * bisector;

        var entryRotation = Rotation3<double>.Slerp(prev.Rotation, vertex.Rotation, (edgeIn - setback) / edgeIn);
        var exitRotation = Rotation3<double>.Slerp(vertex.Rotation, next.Rotation, setback / edgeOut);
        var viaRotation = Rotation3<double>.Slerp(entryRotation, exitRotation, 0.5);

        return (new Pose3<double>(entryTangent, entryRotation),
                new Pose3<double>(via, viaRotation),
                new Pose3<double>(exitTangent, exitRotation));
    }
}
