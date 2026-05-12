using ModelingEvolution.Drawing;
using RocketWelder.SDK.Automation;

namespace RocketWelder.SDK.Robotics.Core.Tests;

public class SimulatedRobotExtensionsTests
{
    private static readonly double[] TinyRadii = { 5, 5, 5, 5, 5, 5 };

    private static RobotModel NonDegenerateRobot()
    {
        var chain = new[]
        {
            DhJoint.FromDegrees(0,    0,   500, 0),
            DhJoint.FromDegrees(-90,  0,   200, 0),
            DhJoint.FromDegrees(0,    500, 0,   0),
            DhJoint.FromDegrees(0,    400, 0,   0),
            DhJoint.FromDegrees(-90,  0,   100, 0),
            DhJoint.FromDegrees(90,   0,   80,  0),
        };
        var limits = Enumerable.Repeat(new JointLimit(-360, 360), 6).ToArray();
        return new RobotModel("SyntheticTestBot", chain, limits, Joints6<double>.Zero);
    }

    private static CollisionEnvironment EnvWith(params CollisionPrimitive[] prims) =>
        new(new PrimitiveCollisionSource(prims), TinyRadii, ToolModel.None);

    private static SimulatedRobot Connected(RobotModel model, CollisionEnvironment? env = null)
    {
        var r = new SimulatedRobot(model, environment: env);
        r.Connect();
        return r;
    }

    // --- Teaching points ----------------------------------------------------

    [Fact]
    public void GetTeachingPoint_Throws_WhenNoSetAttached()
    {
        using var r = Connected(NonDegenerateRobot());
        Action act = () => r.GetTeachingPoint("home");
        act.Should().Throw<InvalidOperationException>().WithMessage("*TeachingPointSet*");
    }

    [Fact]
    public void TryGetTeachingPoint_ReturnsFalse_WhenNoSetAttached()
    {
        using var r = Connected(NonDegenerateRobot());
        r.TryGetTeachingPoint("home", out var pose).Should().BeFalse();
        pose.Should().Be(default(Pose3<double>));
    }

    [Fact]
    public void AttachTeachingPoints_ThenCrudOperations_Roundtrip()
    {
        using var r = Connected(NonDegenerateRobot());
        var set = new TeachingPointSet();
        r.AttachTeachingPoints(set);

        var p = new Pose3<double>(100, 200, 300, 0, 0, 0);
        r.SetTeachingPoint("A", p);
        r.GetTeachingPoint("A").Should().Be(p);
        r.TryGetTeachingPoint("A", out var back).Should().BeTrue();
        back.Should().Be(p);

        r.RemoveTeachingPoint("A").Should().BeTrue();
        r.RemoveTeachingPoint("A").Should().BeFalse();
        r.TryGetTeachingPoint("A", out _).Should().BeFalse();
    }

    [Fact]
    public void GetTeachingPoint_Throws_WhenNameUnknown_WithSetAttached()
    {
        using var r = Connected(NonDegenerateRobot());
        r.AttachTeachingPoints(new TeachingPointSet());
        Action act = () => r.GetTeachingPoint("ghost");
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void SetTeachingPoint_Throws_WhenNoSetAttached()
    {
        using var r = Connected(NonDegenerateRobot());
        Action act = () => r.SetTeachingPoint("A", default);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RemoveTeachingPoint_Throws_WhenNoSetAttached()
    {
        using var r = Connected(NonDegenerateRobot());
        Action act = () => r.RemoveTeachingPoint("A");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AttachTeachingPoints_NullArg_Throws()
    {
        using var r = Connected(NonDegenerateRobot());
        Action act = () => r.AttachTeachingPoints(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // --- Collision-aware moves (MoveResult contract per ADR-004) -------------

    [Fact]
    public void MoveJoint_WithoutEnvironment_AcceptsAnyReachableTarget()
    {
        using var r = Connected(NonDegenerateRobot());
        var result = r.MoveJoint(new Joints6<double>(10, -20, 30, 0, 45, 0));
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void MoveJoint_WithEnvironment_RejectsCollidingTarget_NoThrow()
    {
        var box = new BoxPrimitive("Wall", new Point3<double>(0, 0, 0), 10_000, 10_000, 10_000);
        using var r = Connected(NonDegenerateRobot(), EnvWith(box));
        var beforeJoints = r.GetJointPositions();

        var result = r.MoveJoint(new Joints6<double>(0, 0, 0, 0, 0, 0));
        result.Success.Should().BeFalse();
        result.Reason.Should().Be(MoveFailureReason.Collision);
        result.Collision.Should().NotBeNull();
        r.GetJointPositions().Should().Be(beforeJoints, "state must be unchanged on collision");
    }

    [Fact]
    public void MoveLin_WithEnvironment_RejectsCollidingTarget_NoThrow()
    {
        var box = new BoxPrimitive("Wall", new Point3<double>(0, 0, 0), 10_000, 10_000, 10_000);
        using var r = Connected(NonDegenerateRobot(), EnvWith(box));

        var actualPose = r.GetActualPose();
        var result = r.MoveLin(actualPose, Velocity.Percentage(50));
        result.Success.Should().BeFalse();
        result.Reason.Should().Be(MoveFailureReason.Collision);
        result.Collision.Should().NotBeNull();
    }

    [Fact]
    public void MoveJoint_WithJointLimitViolation_ReturnsJointLimitsExceeded_NotCollision()
    {
        var box = new BoxPrimitive("Wall", new Point3<double>(0, 0, 0), 10_000, 10_000, 10_000);
        using var r = Connected(NonDegenerateRobot(), EnvWith(box));

        var result = r.MoveJoint(new Joints6<double>(500, 0, 0, 0, 0, 0));
        result.Success.Should().BeFalse();
        result.Reason.Should().Be(MoveFailureReason.JointLimitsExceeded);
    }

    // --- IRobot adapter (legacy int/void signatures) --------------------------

    [Fact]
    public void IRobotMoveLin_OnCollision_ReturnsMinusTwo()
    {
        var box = new BoxPrimitive("Wall", new Point3<double>(0, 0, 0), 10_000, 10_000, 10_000);
        using var r = Connected(NonDegenerateRobot(), EnvWith(box));

        ((IRobot)r).MoveLin(r.GetActualPose(), Velocity.Percentage(50)).Should().Be(-2);
    }

    [Fact]
    public void IRobotMoveJoint_OnCollision_DoesNotThrow_StateUnchanged()
    {
        var box = new BoxPrimitive("Wall", new Point3<double>(0, 0, 0), 10_000, 10_000, 10_000);
        using var r = Connected(NonDegenerateRobot(), EnvWith(box));
        var before = r.GetJointPositions();

        Action act = () => ((IRobot)r).MoveJoint(new Joints6<double>(0, 0, 0, 0, 0, 0));
        act.Should().NotThrow();
        r.GetJointPositions().Should().Be(before);
    }

    // --- ExecuteWaypoints collision -----------------------------------------

    [Fact]
    public void ExecuteWaypoints_WithEnvironment_FailsByCollision()
    {
        var box = new BoxPrimitive("Wall", new Point3<double>(0, 0, 0), 10_000, 10_000, 10_000);
        using var r = Connected(NonDegenerateRobot(), EnvWith(box));

        var result = r.ExecuteWaypoints(new[] { r.GetActualPose() }, Velocity.Percentage(50));
        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be(MoveFailureReason.Collision);
        result.Collision.Should().NotBeNull();
        result.FailedWaypointIndex.Should().Be(0);
    }

    [Fact]
    public void ExecuteWaypoints_WithSafeEndpointsButCollidingIntermediate_TruncatesAtLastSafeStep()
    {
        // Box placed along the J1=45° sweep, away from J1=0 and J1=90. Endpoints are
        // collision-free; only intermediate interpolated joint states hit it. This
        // regression asserts that path-level collision detection (not just target-only)
        // catches the hit and the trajectory is truncated at the last safe sub-step.
        var box = new BoxPrimitive("MidSwingObstacle", new Point3<double>(707, 707, 500), 150, 150, 400);
        var model = NonDegenerateRobot();
        var env = new CollisionEnvironment(new PrimitiveCollisionSource(box), TinyRadii, ToolModel.None);

        using var r = new SimulatedRobot(model, environment: env);
        r.Connect();
        var startPose = r.GetActualPose();

        using var probe = new SimulatedRobot(model);
        probe.Connect();
        probe.MoveJoint(new Joints6<double>(90, 0, 0, 0, 0, 0));
        var targetPose = probe.GetActualPose();

        // Sanity: endpoints are collision-free — so only path-level (intermediate) detection can catch this.
        CollisionDetector.CheckCollision(model, Joints6<double>.Zero, env).Should().BeEmpty();
        CollisionDetector.CheckCollision(model, new Joints6<double>(90, 0, 0, 0, 0, 0), env).Should().BeEmpty();

        var result = r.ExecuteWaypoints(new[] { startPose, targetPose }, Velocity.Percentage(50));

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be(MoveFailureReason.Collision);
        result.Collision.Should().NotBeNull();
        result.FailedWaypointIndex.Should().Be(1);

        foreach (var step in result.Steps)
        {
            CollisionDetector.CheckCollision(model, step.Joints, env)
                .Should().BeEmpty("every recorded step must be collision-free (truncate-on-collision)");
        }
    }

    // --- Program execution ---------------------------------------------------

    [Fact]
    public void Execute_RunsEveryStep_AndStepsCoverTrajectory()
    {
        using var r = Connected(NonDegenerateRobot());
        var program = new RobotProgram();
        program.AddMoveJoint(new Joints6<double>(10, -10, 20, 0, 0, 0));
        program.AddMoveJoint(new Joints6<double>(20, -15, 25, 0, 0, 0));

        var result = r.Execute(program, Velocity.Percentage(50));
        result.Success.Should().BeTrue();
        // 1 initial + at least 1 intermediate per step → > program.Count
        result.Steps.Count.Should().BeGreaterThan(program.Count);
        result.Steps[0].Joints.Should().Be(Joints6<double>.Zero);
        result.Steps[^1].Joints.Should().Be(new Joints6<double>(20, -15, 25, 0, 0, 0));
    }

    [Fact]
    public void Execute_WithStartIndex_SkipsEarlierSteps()
    {
        using var r = Connected(NonDegenerateRobot());
        var program = new RobotProgram();
        program.AddMoveJoint(new Joints6<double>(90, 0, 0, 0, 0, 0));   // skipped
        program.AddMoveJoint(new Joints6<double>(0, -20, 0, 0, 0, 0));  // first executed

        var result = r.Execute(program, Velocity.Percentage(50), startIndex: 1);
        result.Success.Should().BeTrue();
        ((double)result.Steps[^1].Joints.J1).Should().Be(0);
        ((double)result.Steps[^1].Joints.J2).Should().Be(-20);
    }

    [Fact]
    public void Execute_StartIndexOutOfRange_Throws()
    {
        using var r = Connected(NonDegenerateRobot());
        var program = new RobotProgram();
        program.AddMoveJoint(new Joints6<double>(10, 0, 0, 0, 0, 0));

        Action neg = () => r.Execute(program, Velocity.Percentage(50), -1);
        Action past = () => r.Execute(program, Velocity.Percentage(50), 2);
        neg.Should().Throw<ArgumentOutOfRangeException>();
        past.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Execute_MoveLinStep_InterpolatesToPose()
    {
        using var r = Connected(NonDegenerateRobot());
        var reachable = r.GetActualPose();
        var program = new RobotProgram();
        program.AddMoveLin(reachable);

        var result = r.Execute(program, Velocity.Percentage(50));
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Execute_StopsOnCollisionAndReportsIndex()
    {
        var box = new BoxPrimitive("Wall", new Point3<double>(0, 0, 0), 10_000, 10_000, 10_000);
        using var r = Connected(NonDegenerateRobot(), EnvWith(box));
        var program = new RobotProgram();
        program.AddMoveJoint(new Joints6<double>(0, 0, 0, 0, 0, 0));

        var result = r.Execute(program, Velocity.Percentage(50));
        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be(MoveFailureReason.Collision);
        result.FailedWaypointIndex.Should().Be(0);
    }

    [Fact]
    public void Execute_NullProgram_Throws()
    {
        using var r = Connected(NonDegenerateRobot());
        Action act = () => r.Execute(null!, Velocity.Percentage(50));
        act.Should().Throw<ArgumentNullException>();
    }
}
