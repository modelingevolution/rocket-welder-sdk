using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core.Tests;

/// <summary>TASK-006 — RobotProgram + ProgramStep + JSON.</summary>
public class RobotProgramTests
{
    [Fact]
    public void AddMoveJoint_And_AddMoveLin_Should_Append_In_Order()
    {
        var program = new RobotProgram();
        program.AddMoveJoint(new Joints6<double>(10, 0, 0, 0, 0, 0));
        program.AddMoveLin(new Pose3<double>(400, 0, 300, 180, 0, 0));
        program.AddMoveJoint(new Joints6<double>(0, 10, 0, 0, 0, 0));

        program.Count.Should().Be(3);
        program.Steps[0].Should().BeOfType<MoveJointStep>();
        program.Steps[1].Should().BeOfType<MoveLinStep>();
        program.Steps[2].Should().BeOfType<MoveJointStep>();
    }

    [Fact]
    public void InsertAt_Should_Insert_At_Index()
    {
        var program = new RobotProgram();
        program.AddMoveLin(new Pose3<double>(1, 0, 0, 0, 0, 0));
        program.AddMoveLin(new Pose3<double>(3, 0, 0, 0, 0, 0));

        program.InsertAt(1, new MoveLinStep(new Pose3<double>(2, 0, 0, 0, 0, 0)));

        program.Count.Should().Be(3);
        ((MoveLinStep)program.Steps[1]).Target.X.Should().Be(2);
    }

    [Fact]
    public void RemoveAt_Should_Drop_Step()
    {
        var program = new RobotProgram();
        program.AddMoveLin(new Pose3<double>(1, 0, 0, 0, 0, 0));
        program.AddMoveLin(new Pose3<double>(2, 0, 0, 0, 0, 0));

        program.RemoveAt(0);

        program.Count.Should().Be(1);
        ((MoveLinStep)program.Steps[0]).Target.X.Should().Be(2);
    }

    [Fact]
    public void ReplaceAt_Should_Overwrite_Step()
    {
        var program = new RobotProgram();
        program.AddMoveJoint(new Joints6<double>(0, 0, 0, 0, 0, 0));
        program.AddMoveJoint(new Joints6<double>(1, 0, 0, 0, 0, 0));

        program.ReplaceAt(1, new MoveLinStep(new Pose3<double>(500, 0, 0, 0, 0, 0)));

        program.Steps[1].Should().BeOfType<MoveLinStep>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void InsertAt_Out_Of_Range_Should_Throw(int index)
    {
        var program = new RobotProgram();
        program.AddMoveJoint(Joints6<double>.Zero);
        program.AddMoveJoint(Joints6<double>.Zero);

        var act = () => program.InsertAt(index, new MoveJointStep(Joints6<double>.Zero));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void RemoveAt_Out_Of_Range_Should_Throw(int index)
    {
        var program = new RobotProgram();
        program.AddMoveJoint(Joints6<double>.Zero);
        program.AddMoveJoint(Joints6<double>.Zero);

        var act = () => program.RemoveAt(index);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void ReplaceAt_Out_Of_Range_Should_Throw(int index)
    {
        var program = new RobotProgram();
        program.AddMoveJoint(Joints6<double>.Zero);
        program.AddMoveJoint(Joints6<double>.Zero);

        var act = () => program.ReplaceAt(index, new MoveJointStep(Joints6<double>.Zero));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void JSON_RoundTrip_Should_Preserve_Step_Types_And_Targets()
    {
        var program = new RobotProgram();
        program.AddMoveJoint(new Joints6<double>(10, -20, 30, -40, 50, -60));
        program.AddMoveLin(new Pose3<double>(400.5, 100.25, 300.75, 180, 0, 45));
        program.AddMoveJoint(new Joints6<double>(0, 0, 0, 0, 0, 0));

        var json = program.ToJson();
        var restored = RobotProgram.FromJson(json);

        restored.Count.Should().Be(3);
        restored.Steps[0].Should().BeOfType<MoveJointStep>();
        restored.Steps[1].Should().BeOfType<MoveLinStep>();
        restored.Steps[2].Should().BeOfType<MoveJointStep>();

        var mj0 = (MoveJointStep)restored.Steps[0];
        mj0.Target.Should().Be(new Joints6<double>(10, -20, 30, -40, 50, -60));

        var ml = (MoveLinStep)restored.Steps[1];
        ml.Target.X.Should().BeApproximately(400.5, 1e-9);
        ((double)ml.Target.Rz).Should().BeApproximately(45, 1e-9);
    }

    [Fact]
    public void JSON_Should_Emit_Type_Discriminator()
    {
        var program = new RobotProgram();
        program.AddMoveJoint(Joints6<double>.Zero);
        program.AddMoveLin(new Pose3<double>(100, 0, 0, 0, 0, 0));

        var json = program.ToJson();

        json.Should().Contain("\"type\":\"moveJoint\"");
        json.Should().Contain("\"type\":\"moveLin\"");
    }

    [Fact]
    public void Add_Null_Step_Should_Throw()
    {
        var program = new RobotProgram();
        var act = () => program.Add(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
