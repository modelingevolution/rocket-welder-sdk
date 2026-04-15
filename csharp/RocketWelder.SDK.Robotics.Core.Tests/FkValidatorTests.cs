using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core.Tests;

/// <summary>TASK-010 — FkValidator import/export + Validate.</summary>
public class FkValidatorTests
{
    [Fact]
    public void Export_Writes_Schema_Version_And_Units()
    {
        var records = new List<FkValidationRecord>
        {
            new(new Joints6<double>(10, -20, 30, -40, 50, -60),
                new Pose3<double>(400, 0, 300, 180, 0, 0))
        };

        var json = FkValidator.Export(records);

        json.Should().Contain("\"schemaVersion\":\"1\"");
        json.Should().Contain("\"units\"");
        json.Should().Contain("\"joints\":\"degrees\"");
        json.Should().Contain("\"position\":\"mm\"");
        json.Should().Contain("\"rotation\":\"degrees\"");
    }

    [Fact]
    public void Import_RoundTrip_Should_Preserve_Records()
    {
        var original = new List<FkValidationRecord>
        {
            new(new Joints6<double>(10, -20, 30, -40, 50, -60),
                new Pose3<double>(400.5, 100.25, 300.75, 180, 0, 45)),
            new(new Joints6<double>(0, 0, 0, 0, 0, 0),
                new Pose3<double>(0, 0, 0, 0, 0, 0))
        };

        var json = FkValidator.Export(original);
        var restored = FkValidator.Import(json);

        restored.Should().HaveCount(2);
        restored[0].Joints.Should().Be(original[0].Joints);
        restored[0].TcpPose.X.Should().BeApproximately(400.5, 1e-9);
        ((double)restored[0].TcpPose.Rz).Should().BeApproximately(45, 1e-9);
    }

    [Fact]
    public void Import_Unsupported_Schema_Version_Should_Throw()
    {
        var badJson = """{"schemaVersion":"99","units":{"joints":"degrees","position":"mm","rotation":"degrees"},"records":[]}""";

        var act = () => FkValidator.Import(badJson);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*99*");
    }

    [Fact]
    public void Import_Null_Should_Throw()
    {
        var act = () => FkValidator.Import(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Validate_Records_Generated_By_Same_Model_Should_Have_Zero_Error()
    {
        var model = RobotPresets.FairinoFR5();
        var joints = new Joints6<double>[]
        {
            new(0, 0, 0, 0, 0, 0),
            new(10, -20, 30, -40, 50, -60),
            new(-90, 45, -45, 0, 90, 0)
        };
        var records = joints.Select(j =>
        {
            var state = ForwardKinematics.Compute(model, j);
            return new FkValidationRecord(j, state.TcpPose);
        }).ToList();

        var results = FkValidator.Validate(model, records);

        results.Should().HaveCount(3);
        foreach (var r in results)
        {
            r.EuclideanDistance.Should().BeApproximately(0, 1e-6);
            Math.Abs(r.DX).Should().BeLessThan(1e-6);
            Math.Abs(r.DY).Should().BeLessThan(1e-6);
            Math.Abs(r.DZ).Should().BeLessThan(1e-6);
            Math.Abs(r.DRx).Should().BeLessThan(1e-6);
            Math.Abs(r.DRy).Should().BeLessThan(1e-6);
            Math.Abs(r.DRz).Should().BeLessThan(1e-6);
        }
    }

    [Fact]
    public void Validate_Known_Position_Offset_Should_Report_Correct_Euclidean()
    {
        var model = RobotPresets.FairinoFR5();
        var joints = new Joints6<double>(0, 0, 0, 0, 0, 0);
        var truth = ForwardKinematics.Compute(model, joints).TcpPose;

        // Shift expected pose by (+3, +4, 0) — Euclidean = 5.
        var shifted = new Pose3<double>(truth.X + 3, truth.Y + 4, truth.Z, truth.Rx, truth.Ry, truth.Rz);
        var records = new List<FkValidationRecord> { new(joints, shifted) };

        var results = FkValidator.Validate(model, records);

        results.Should().HaveCount(1);
        results[0].Index.Should().Be(0);
        results[0].DX.Should().BeApproximately(3, 1e-6);
        results[0].DY.Should().BeApproximately(4, 1e-6);
        results[0].DZ.Should().BeApproximately(0, 1e-6);
        results[0].EuclideanDistance.Should().BeApproximately(5, 1e-6);
    }

    [Fact]
    public void Validate_Rotation_Wrap_Should_Report_Small_Delta_Across_Pm180()
    {
        var model = RobotPresets.FairinoFR5();
        var joints = new Joints6<double>(0, 0, 0, 0, 0, 0);
        var truth = ForwardKinematics.Compute(model, joints).TcpPose;

        // Simulate a controller reporting +179 while FK yields -179 — wrapped delta is 2°, not 358°.
        var aRx = (double)truth.Rx;
        var shiftedRx = aRx + 358; // differs by 358 but wraps to -2
        var shifted = new Pose3<double>(truth.X, truth.Y, truth.Z, shiftedRx, (double)truth.Ry, (double)truth.Rz);
        var records = new List<FkValidationRecord> { new(joints, shifted) };

        var results = FkValidator.Validate(model, records);

        Math.Abs(results[0].DRx).Should().BeLessThan(2.001);
    }

    [Fact]
    public void Validate_Null_Model_Should_Throw()
    {
        var records = new List<FkValidationRecord>();
        var act = () => FkValidator.Validate(null!, records);
        act.Should().Throw<ArgumentNullException>();
    }
}
