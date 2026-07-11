using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core.Tests;

/// <summary>TASK-001 — MoveResult evolution with MoveFailureReason and optional CollisionResult.</summary>
public class MoveResultTests
{
    [Fact]
    public void Succeeded_Should_Have_No_Reason_No_Collision_No_Violations()
    {
        var result = MoveResult.Succeeded();

        result.Success.Should().BeTrue();
        result.Reason.Should().BeNull();
        result.Collision.Should().BeNull();
        result.Violations.Should().BeNull();
    }

    [Theory]
    [InlineData(MoveFailureReason.OutOfReach)]
    [InlineData(MoveFailureReason.JointLimitsExceeded)]
    [InlineData(MoveFailureReason.Singularity)]
    [InlineData(MoveFailureReason.NoConvergence)]
    [InlineData(MoveFailureReason.Collision)]
    public void Failed_Should_Carry_Reason(MoveFailureReason reason)
    {
        var result = MoveResult.Failed(reason);

        result.Success.Should().BeFalse();
        result.Reason.Should().Be(reason);
    }

    [Fact]
    public void Failed_With_Violations_Should_Carry_Violations()
    {
        var violations = new[] { new JointLimitViolation(1, 85, 120, 35) };

        var result = MoveResult.Failed(MoveFailureReason.JointLimitsExceeded, violations);

        result.Violations.Should().BeEquivalentTo(violations);
        result.Collision.Should().BeNull();
    }

    [Fact]
    public void RejectedByCollision_Should_Carry_CollisionResult()
    {
        var collision = new CollisionResult("Link3", "Box1", 12.5,
            new Point3<double>(100, 200, 50));

        var result = MoveResult.RejectedByCollision(collision);

        result.Success.Should().BeFalse();
        result.Reason.Should().Be(MoveFailureReason.Collision);
        result.Collision.Should().Be(collision);
        result.Violations.Should().BeNull();
    }

    [Fact]
    public void MoveFailureReason_Should_Cover_Every_IkFailureReason()
    {
        // Every IkFailureReason must have a corresponding MoveFailureReason with the same name.
        foreach (IkFailureReason ik in Enum.GetValues<IkFailureReason>())
        {
            var match = Enum.TryParse<MoveFailureReason>(ik.ToString(), out _);
            match.Should().BeTrue($"MoveFailureReason must define {ik}");
        }

        Enum.IsDefined(typeof(MoveFailureReason), nameof(MoveFailureReason.Collision)).Should().BeTrue();
    }
}
