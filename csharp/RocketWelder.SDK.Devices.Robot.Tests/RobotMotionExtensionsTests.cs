using ModelingEvolution.Drawing;
using NSubstitute;

namespace RocketWelder.SDK.Devices.Robot.Tests;

public class RobotMotionExtensionsTests
{
    private static readonly Velocity Turn = Velocity.MmPerSecond(4);
    private static readonly Velocity Travel = Velocity.MmPerSecond(8);

    private static Pose3<double> At(double x, double y, double z) => new(x, y, z, 0, 0, 0);

    private static bool Near(Point3<double> p, double x, double y, double z, double tol = 1e-6)
        => Math.Abs(p.X - x) < tol && Math.Abs(p.Y - y) < tol && Math.Abs(p.Z - z) < tol;

    [Fact]
    public void MoveCorner_Should_Lin_To_Entry_Then_Arc_To_Exit_On_RightAngle()
    {
        // Arrange — a 90° corner at the origin, edges of length 10 along +Y and +X, radius 2.
        var robot = Substitute.For<IRobot>();
        var prev = At(0, 10, 0);
        var vertex = At(0, 0, 0);
        var next = At(10, 0, 0);

        // Act
        robot.MoveCorner(prev, vertex, next, radiusMm: 2.0, turn: Turn, travel: Travel);

        // Assert — setback t = r/tan(45°) = 2; tangents at (0,2,0) and (2,0,0); arc via (2-√2, 2-√2, 0).
        var via = 2.0 - Math.Sqrt(2.0);
        Received.InOrder(() =>
        {
            robot.MoveLin(Arg.Is<Pose3<double>>(p => Near(p.Position, 0, 2, 0)), Travel);
            robot.MoveCircular(
                Arg.Is<Pose3<double>>(p => Near(p.Position, via, via, 0)),
                Arg.Is<Pose3<double>>(p => Near(p.Position, 2, 0, 0)),
                Turn);
        });
        robot.Received(1).MoveLin(Arg.Any<Pose3<double>>(), Arg.Any<Velocity>());
        robot.Received(1).MoveCircular(Arg.Any<Pose3<double>>(), Arg.Any<Pose3<double>>(), Arg.Any<Velocity>());
    }

    [Fact]
    public void MoveCorner_Arc_Endpoints_Should_Be_RadiusMm_From_Arc_Centre()
    {
        // Arrange — non-symmetric edges to exercise the closed-form fillet centre.
        var robot = Substitute.For<IRobot>();
        var prev = At(-6, 0, 0);
        var vertex = At(0, 0, 0);
        var next = At(0, 9, 0);
        const double radius = 2.5;

        Pose3<double>? via = null, exit = null, entry = null;
        robot.MoveLin(Arg.Do<Pose3<double>>(p => entry = p), Arg.Any<Velocity>());
        robot.MoveCircular(Arg.Do<Pose3<double>>(p => via = p), Arg.Do<Pose3<double>>(p => exit = p), Arg.Any<Velocity>());

        // Act
        robot.MoveCorner(prev, vertex, next, radius, Turn, Travel);

        // Assert — both tangents lie on the fillet circle: the via point bisects the arc, so the circle
        // centre is equidistant (= radius) from both tangents. Reconstruct the centre and verify.
        entry.Should().NotBeNull();
        via.Should().NotBeNull();
        exit.Should().NotBeNull();

        // Entry on +X-from-vertex edge, exit on +Y edge: 90° corner, t = r/tan(45°) = r.
        Near(entry!.Value.Position, -radius, 0, 0).Should().BeTrue();
        Near(exit!.Value.Position, 0, radius, 0).Should().BeTrue();
        // Arc via is inside the corner on the bisector at distance r·(1 - cos(45°))·? — verify it is exactly
        // radius from the centre (-r, r, 0).
        var centre = new Point3<double>(-radius, radius, 0);
        Point3<double>.Distance(centre, via!.Value.Position).Should().BeApproximately(radius, 1e-6);
    }

    [Fact]
    public void MoveCorner_Should_Pass_Straight_Through_When_NearCollinear()
    {
        // Arrange — bend well under the 2° gate: treated as straight.
        var robot = Substitute.For<IRobot>();
        var prev = At(0, 0, 0);
        var vertex = At(10, 0, 0);
        var next = At(20, 0.05, 0);

        // Act
        robot.MoveCorner(prev, vertex, next, radiusMm: 2.0, turn: Turn, travel: Travel);

        // Assert — one straight move to the vertex, no arc.
        robot.Received(1).MoveLin(Arg.Is<Pose3<double>>(p => Near(p.Position, 10, 0, 0)), Travel);
        robot.DidNotReceive().MoveCircular(Arg.Any<Pose3<double>>(), Arg.Any<Pose3<double>>(), Arg.Any<Velocity>());
    }

    [Fact]
    public void MoveCorner_Should_Pass_Straight_Through_When_Radius_Is_Zero()
    {
        // Arrange — a genuine 90° corner but zero radius: sharp, no fillet.
        var robot = Substitute.For<IRobot>();

        // Act
        robot.MoveCorner(At(0, 10, 0), At(0, 0, 0), At(10, 0, 0), radiusMm: 0, turn: Turn, travel: Travel);

        // Assert
        robot.Received(1).MoveLin(Arg.Is<Pose3<double>>(p => Near(p.Position, 0, 0, 0)), Travel);
        robot.DidNotReceive().MoveCircular(Arg.Any<Pose3<double>>(), Arg.Any<Pose3<double>>(), Arg.Any<Velocity>());
    }

    [Fact]
    public void MoveCorner_Should_Throw_When_Radius_Does_Not_Fit()
    {
        // Arrange — 90° corner, edges of length 10. Max-fit radius = 10·tan(45°) = 10; ask for 50.
        var robot = Substitute.For<IRobot>();

        // Act
        var act = () => robot.MoveCorner(At(0, 10, 0), At(0, 0, 0), At(10, 0, 0), radiusMm: 50, turn: Turn, travel: Travel);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
        robot.DidNotReceive().MoveLin(Arg.Any<Pose3<double>>(), Arg.Any<Velocity>());
        robot.DidNotReceive().MoveCircular(Arg.Any<Pose3<double>>(), Arg.Any<Pose3<double>>(), Arg.Any<Velocity>());
    }

    [Fact]
    public void MoveCorner_Should_Throw_When_Edge_Has_Zero_Length()
    {
        // Arrange — vertex coincides with next.
        var robot = Substitute.For<IRobot>();

        // Act
        var act = () => robot.MoveCorner(At(0, 10, 0), At(0, 0, 0), At(0, 0, 0), radiusMm: 2, turn: Turn, travel: Travel);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MoveCorner_Should_Throw_On_Null_Robot()
    {
        // Arrange
        IRobot robot = null!;

        // Act
        var act = () => robot.MoveCorner(At(0, 10, 0), At(0, 0, 0), At(10, 0, 0), radiusMm: 2, turn: Turn, travel: Travel);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
