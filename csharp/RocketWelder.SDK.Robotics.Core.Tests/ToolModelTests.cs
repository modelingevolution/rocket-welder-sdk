namespace RocketWelder.SDK.Robotics.Core.Tests;

/// <summary>TASK-005 — ToolModel hierarchy.</summary>
public class ToolModelTests
{
    [Fact]
    public void CapsuleToolModel_Should_Carry_Dimensions()
    {
        var tool = new CapsuleToolModel(120.0, 15.0);

        tool.Length.Should().Be(120.0);
        tool.Radius.Should().Be(15.0);
    }

    [Fact]
    public void CapsuleToolModel_Equality_Should_Compare_By_Value()
    {
        var a = new CapsuleToolModel(50, 10);
        var b = new CapsuleToolModel(50, 10);
        var c = new CapsuleToolModel(50, 11);

        a.Should().Be(b);
        a.Should().NotBe(c);
    }

    [Fact]
    public void MeshToolModel_Should_Carry_Opaque_Id()
    {
        var tool = new MeshToolModel("torch-v3.glb");

        tool.Id.Should().Be("torch-v3.glb");
    }

    [Fact]
    public void MeshToolModel_Equality_Should_Compare_By_Id()
    {
        var a = new MeshToolModel("torch");
        var b = new MeshToolModel("torch");
        var c = new MeshToolModel("gripper");

        a.Should().Be(b);
        a.Should().NotBe(c);
    }

    [Fact]
    public void ToolModel_None_Should_Be_A_ToolModel()
    {
        ToolModel.None.Should().NotBeNull();
        ToolModel.None.Should().BeAssignableTo<ToolModel>();
    }

    [Fact]
    public void ToolModel_Variants_Should_Be_Pattern_Matchable()
    {
        ToolModel tool = new CapsuleToolModel(100, 20);

        var result = tool switch
        {
            CapsuleToolModel c => $"capsule:{c.Length}:{c.Radius}",
            MeshToolModel m => $"mesh:{m.Id}",
            _ => "none"
        };

        result.Should().Be("capsule:100:20");
    }
}
