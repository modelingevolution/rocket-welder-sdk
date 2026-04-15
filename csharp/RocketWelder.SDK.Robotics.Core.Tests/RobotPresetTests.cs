using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core.Tests;

/// <summary>Test 1.1 — Fairino v6 preset factories load correct parameters per fairino-preset-reference.md.</summary>
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
        var expected = new (double min, double max)[]
        {
            (-178, 178), (-265, 85), (-162, 162), (-265, 85), (-178, 178), (-360, 360)
        };
        model.JointLimits.Count.Should().Be(6);
        for (int i = 0; i < 6; i++)
        {
            model.JointLimits[i].MinDeg.Should().Be(expected[i].min, $"J{i + 1} min");
            model.JointLimits[i].MaxDeg.Should().Be(expected[i].max, $"J{i + 1} max");
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
        // d values per corrected DH: d1=152, d2=d3=d4=0, d5=102.1, d6=102.0
        model.DhChain[0].D.Should().BeApproximately(152.0, 0.001);
        model.DhChain[1].D.Should().BeApproximately(0, 0.001);
        model.DhChain[2].D.Should().BeApproximately(0, 0.001);
        model.DhChain[3].D.Should().BeApproximately(0, 0.001);
        model.DhChain[4].D.Should().BeApproximately(102.1, 0.001);
        model.DhChain[5].D.Should().BeApproximately(102.0, 0.001);
    }

    [Fact]
    public void FairinoFR5_DhChain_Should_Have_Correct_A_Values()
    {
        var model = RobotPresets.FairinoFR5();
        // a values per corrected DH: a(J1..J6) = 0, 0, 0, -425, -395.01, 0, 0
        // Mapped to chain rows (a_{i-1}): row0=0, row1=0, row2=0, row3=-425, row4=-395.01, row5=0
        model.DhChain[0].A.Should().BeApproximately(0, 0.001);
        model.DhChain[1].A.Should().BeApproximately(0, 0.001);
        model.DhChain[2].A.Should().BeApproximately(-425.0, 0.001);
        model.DhChain[3].A.Should().BeApproximately(-395.01, 0.001);
        model.DhChain[4].A.Should().BeApproximately(0, 0.001);
        model.DhChain[5].A.Should().BeApproximately(0, 0.001);
    }

    [Theory]
    [InlineData("Fairino FR3", 140.0, 280.0, 240.01, 102.0, 102.0)]
    [InlineData("Fairino FR5", 152.0, 425.0, 395.01, 102.1, 102.0)]
    [InlineData("Fairino FR10", 180.0, 700.0, 586.0, 159.0, 114.0)]
    [InlineData("Fairino FR16", 180.0, 520.0, 400.0, 159.0, 114.0)]
    [InlineData("Fairino FR20", 215.0, 1000.0, 716.0, 166.01, 138.0)]
    [InlineData("Fairino FR30", 215.0, 700.0, 536.0, 166.01, 138.0)]
    public void FairinoV6_Presets_Should_Have_Reference_LinkLengths(
        string expectedName, double d1, double a2, double a3, double d5, double d6)
    {
        var model = expectedName switch
        {
            "Fairino FR3" => RobotPresets.FairinoFR3(),
            "Fairino FR5" => RobotPresets.FairinoFR5(),
            "Fairino FR10" => RobotPresets.FairinoFR10(),
            "Fairino FR16" => RobotPresets.FairinoFR16(),
            "Fairino FR20" => RobotPresets.FairinoFR20(),
            "Fairino FR30" => RobotPresets.FairinoFR30(),
            _ => throw new ArgumentOutOfRangeException(nameof(expectedName)),
        };

        model.Name.Should().Be(expectedName);
        model.DhChain.Count.Should().Be(6);
        model.DhChain[0].D.Should().BeApproximately(d1, 0.001, "d1");
        model.DhChain[2].A.Should().BeApproximately(-a2, 0.001, "a_{J3} = -a2");
        model.DhChain[3].A.Should().BeApproximately(-a3, 0.001, "a_{J4} = -a3");
        model.DhChain[3].D.Should().BeApproximately(0, 0.001, "d4 must be 0 under MDH Craig");
        model.DhChain[4].D.Should().BeApproximately(d5, 0.001, "d5");
        model.DhChain[5].D.Should().BeApproximately(d6, 0.001, "d6");
    }

    /// <summary>
    /// Finding #10 smoke test — FK at HOME on every v6 preset must return a finite
    /// TCP position whose distance from the base origin is within the DH-derived
    /// geometric reach bound (sum of link offsets). No datasheet reach figures are
    /// consulted here — the bound is computed directly from the preset's own DH
    /// chain, so this is a pure self-consistency check.
    /// </summary>
    [Theory]
    [InlineData("Fairino FR3")]
    [InlineData("Fairino FR5")]
    [InlineData("Fairino FR10")]
    [InlineData("Fairino FR16")]
    [InlineData("Fairino FR20")]
    [InlineData("Fairino FR30")]
    public void FairinoV6_Presets_FK_AtHome_ShouldBe_Finite_And_WithinReach(string name)
    {
        var model = name switch
        {
            "Fairino FR3" => RobotPresets.FairinoFR3(),
            "Fairino FR5" => RobotPresets.FairinoFR5(),
            "Fairino FR10" => RobotPresets.FairinoFR10(),
            "Fairino FR16" => RobotPresets.FairinoFR16(),
            "Fairino FR20" => RobotPresets.FairinoFR20(),
            "Fairino FR30" => RobotPresets.FairinoFR30(),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };

        var state = ForwardKinematics.Compute(model, Joints6<double>.Zero);

        double.IsFinite(state.TcpPose.X).Should().BeTrue($"{name} TCP X must be finite");
        double.IsFinite(state.TcpPose.Y).Should().BeTrue($"{name} TCP Y must be finite");
        double.IsFinite(state.TcpPose.Z).Should().BeTrue($"{name} TCP Z must be finite");

        // Triangle-inequality upper bound on |TCP| from the DH chain: sum of |a| and |d|
        // across all six joint rows. 1.1 slack absorbs FK rounding / base-frame offset.
        var geometricReach = 0.0;
        foreach (var j in model.DhChain)
            geometricReach += Math.Abs(j.A) + Math.Abs(j.D);

        var tcpMag = Math.Sqrt(
            state.TcpPose.X * state.TcpPose.X +
            state.TcpPose.Y * state.TcpPose.Y +
            state.TcpPose.Z * state.TcpPose.Z);

        tcpMag.Should().BeLessThanOrEqualTo(geometricReach * 1.1,
            $"{name} TCP magnitude must be within DH-derived reach × 1.1");
    }

    [Theory]
    [InlineData("Fairino FR3")]
    [InlineData("Fairino FR5")]
    [InlineData("Fairino FR10")]
    [InlineData("Fairino FR16")]
    [InlineData("Fairino FR20")]
    [InlineData("Fairino FR30")]
    public void FairinoV6_Presets_Should_Share_V6_JointLimits(string name)
    {
        var model = name switch
        {
            "Fairino FR3" => RobotPresets.FairinoFR3(),
            "Fairino FR5" => RobotPresets.FairinoFR5(),
            "Fairino FR10" => RobotPresets.FairinoFR10(),
            "Fairino FR16" => RobotPresets.FairinoFR16(),
            "Fairino FR20" => RobotPresets.FairinoFR20(),
            "Fairino FR30" => RobotPresets.FairinoFR30(),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };

        var expected = new (double min, double max)[]
        {
            (-178, 178), (-265, 85), (-162, 162), (-265, 85), (-178, 178), (-360, 360)
        };
        for (int i = 0; i < 6; i++)
        {
            model.JointLimits[i].MinDeg.Should().Be(expected[i].min, $"{name} J{i + 1} min");
            model.JointLimits[i].MaxDeg.Should().Be(expected[i].max, $"{name} J{i + 1} max");
        }
    }
}
