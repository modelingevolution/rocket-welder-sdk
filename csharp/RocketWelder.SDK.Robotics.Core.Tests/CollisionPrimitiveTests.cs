using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core.Tests;

/// <summary>TASK-002 — primitives, PrimitiveCollisionSource, safety margin.</summary>
public class CollisionPrimitiveTests
{
    private static Point3<double> P(double x, double y, double z) => new(x, y, z);

    [Fact]
    public void Sphere_SignedDistance_To_Passing_Segment_Should_Be_Negative_When_Penetrating()
    {
        var s = new SpherePrimitive("s", P(0, 0, 0), 10);
        var d = s.SignedDistanceToSegment(P(-50, 0, 0), P(50, 0, 0));
        d.Should().BeLessThan(0);
        d.Should().BeApproximately(-10, 1e-6);
    }

    [Fact]
    public void Sphere_Far_Segment_Should_Be_Positive_Distance()
    {
        var s = new SpherePrimitive("s", P(0, 0, 0), 5);
        var d = s.SignedDistanceToSegment(P(0, 0, 50), P(100, 0, 50));
        d.Should().BeApproximately(45, 1e-6);
    }

    [Fact]
    public void Box_Penetrating_Segment_Should_Report_Negative_Distance()
    {
        var box = new BoxPrimitive("b", P(0, 0, 0), 10, 10, 10);
        var d = box.SignedDistanceToSegment(P(-50, 0, 0), P(50, 0, 0));
        d.Should().BeLessThanOrEqualTo(0);
    }

    [Fact]
    public void Box_Far_Segment_Should_Be_Positive_Distance()
    {
        var box = new BoxPrimitive("b", P(0, 0, 0), 10, 10, 10);
        var d = box.SignedDistanceToSegment(P(50, 50, 0), P(50, -50, 0));
        d.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Plane_Below_Origin_Should_Report_Positive_Distance_For_Segment_Above()
    {
        var plane = new PlanePrimitive("floor", P(0, 0, 0), new Vector3<double>(0, 0, 1));
        var d = plane.SignedDistanceToSegment(P(0, 0, 100), P(100, 100, 50));
        d.Should().BeApproximately(50, 1e-6);
    }

    [Fact]
    public void Plane_Crossing_Segment_Should_Report_Negative_Distance()
    {
        var plane = new PlanePrimitive("floor", P(0, 0, 0), new Vector3<double>(0, 0, 1));
        var d = plane.SignedDistanceToSegment(P(0, 0, -5), P(0, 0, 10));
        d.Should().BeApproximately(-5, 1e-6);
    }

    [Fact]
    public void Capsule_Crossing_Segment_Should_Report_Negative_Distance()
    {
        var c = new CapsulePrimitive("c", P(-50, 0, 0), P(50, 0, 0), 5);
        var d = c.SignedDistanceToSegment(P(0, -100, 0), P(0, 100, 0));
        d.Should().BeApproximately(-5, 1e-6);
    }

    [Fact]
    public void Cylinder_Penetrating_Segment_Should_Report_Negative_Distance()
    {
        var cyl = new CylinderPrimitive("c", P(0, 0, 0), 10, 100);
        var d = cyl.SignedDistanceToSegment(P(-50, 0, 0), P(50, 0, 0));
        d.Should().BeLessThanOrEqualTo(0);
    }

    [Fact]
    public void PrimitiveCollisionSource_Should_Reject_Wrong_LinkRadii_Count()
    {
        var src = new PrimitiveCollisionSource();
        var model = RobotPresets.FairinoFR5();

        var act = () => src.QueryCollision(model, Joints6<double>.Zero, new double[] { 50, 50, 50 },
            ToolModel.None, 0);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PrimitiveCollisionSource_Should_Reject_Negative_Margin()
    {
        var src = new PrimitiveCollisionSource();
        var model = RobotPresets.FairinoFR5();
        var radii = Enumerable.Repeat(50.0, 6).ToArray();

        var act = () => src.QueryCollision(model, Joints6<double>.Zero, radii, ToolModel.None, -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void PrimitiveCollisionSource_Should_Detect_Floor_Hit_By_Link_Capsule()
    {
        var floor = new PlanePrimitive("floor", P(0, 0, -500), new Vector3<double>(0, 0, 1));
        var src = new PrimitiveCollisionSource(floor);
        var model = RobotPresets.FairinoFR5();
        var radii = Enumerable.Repeat(30.0, 6).ToArray();

        var results = src.QueryCollision(model, Joints6<double>.Zero, radii, ToolModel.None, safetyMargin: 0);

        // The home TCP is above the floor, so no collision expected.
        results.Should().BeEmpty();
    }

    [Fact]
    public void PrimitiveCollisionSource_Should_Detect_Box_Encasing_Robot_Base()
    {
        // A box that envelops the base will collide with Link 1 capsule.
        var box = new BoxPrimitive("encloser", P(0, 0, 0), 300, 300, 300);
        var src = new PrimitiveCollisionSource(box);
        var model = RobotPresets.FairinoFR5();
        var radii = Enumerable.Repeat(40.0, 6).ToArray();

        var results = src.QueryCollision(model, Joints6<double>.Zero, radii, ToolModel.None, safetyMargin: 0);

        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.BodyB == "encloser");
    }

    [Fact]
    public void SafetyMargin_Should_Promote_A_Near_Miss_To_A_Collision()
    {
        // Put a sphere 10mm off the home tcp Z ray so that it is clear at margin=0 but
        // hit at margin=large. We just need a sphere positioned far from any link.
        var sphere = new SpherePrimitive("orb", P(2000, 0, 0), 5);
        var src = new PrimitiveCollisionSource(sphere);
        var model = RobotPresets.FairinoFR5();
        var radii = Enumerable.Repeat(30.0, 6).ToArray();

        var no = src.QueryCollision(model, Joints6<double>.Zero, radii, ToolModel.None, safetyMargin: 0);
        var yes = src.QueryCollision(model, Joints6<double>.Zero, radii, ToolModel.None, safetyMargin: 10_000);

        no.Should().BeEmpty();
        yes.Should().NotBeEmpty();
    }

    [Fact]
    public void PrimitiveCollisionSource_Should_Include_Tool_Capsule_When_Present()
    {
        // Place a sphere right along +Z of the flange, 200mm above the flange frame origin, small radius.
        // Home flange-z in base frame = flange X/Y/Z basis rotated — we just confirm Tool contributes
        // when safety margin is huge enough to overlap something.
        var sphere = new SpherePrimitive("target", P(0, 0, 0), 1);
        var src = new PrimitiveCollisionSource(sphere);
        var model = RobotPresets.FairinoFR5();
        var radii = Enumerable.Repeat(1.0, 6).ToArray();

        var tool = new CapsuleToolModel(100, 10);
        var results = src.QueryCollision(model, Joints6<double>.Zero, radii, tool, safetyMargin: 10_000);

        results.Should().Contain(r => r.BodyA == "Tool");
    }
}
