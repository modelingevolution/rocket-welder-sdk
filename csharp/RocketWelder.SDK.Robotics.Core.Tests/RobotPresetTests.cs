using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core.Tests;

/// <summary>Test 1.1 — Robot preset Fairino FR5 loads correct parameters.</summary>
public class RobotPresetTests
{
    [Fact]
    public void FairinoFR5_Should_Have_6_DhJoints()
    {
        var model = RobotPresets.FairinoFR5();
        model.DhChain.Count.Should().Be(6);
    }

    [Fact]
    public void FairinoFR5_Should_Have_Correct_JointLimits()
    {
        var model = RobotPresets.FairinoFR5();
        model.JointLimits.Count.Should().Be(6);
        foreach (var limit in model.JointLimits)
        {
            limit.MinDeg.Should().Be(-175);
            limit.MaxDeg.Should().Be(175);
        }
    }

    [Fact]
    public void FairinoFR5_Should_Have_Zero_HomePosition()
    {
        var model = RobotPresets.FairinoFR5();
        model.HomePosition.Should().Be(Joints6<double>.Zero);
    }

    [Fact]
    public void FairinoFR5_Should_Have_Correct_Name()
    {
        var model = RobotPresets.FairinoFR5();
        model.Name.Should().Be("Fairino FR5");
    }

    [Fact]
    public void FairinoFR5_DhChain_Should_Have_Correct_D_Values()
    {
        var model = RobotPresets.FairinoFR5();
        // d values: 152.0, 0, 0, 115.7, 92.2, 94.0 (Craig convention mapping)
        model.DhChain[0].D.Should().BeApproximately(152.0, 0.001);
        model.DhChain[1].D.Should().BeApproximately(0, 0.001);
        model.DhChain[2].D.Should().BeApproximately(0, 0.001);
        model.DhChain[3].D.Should().BeApproximately(115.7, 0.001);
        model.DhChain[4].D.Should().BeApproximately(92.2, 0.001);
        model.DhChain[5].D.Should().BeApproximately(94.0, 0.001);
    }
}
