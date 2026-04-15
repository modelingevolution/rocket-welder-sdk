using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core.Tests;

public class CollisionDetectorTests
{
    private static readonly double[] NormalRadii = { 60, 60, 45, 35, 25, 20 };
    private static readonly double[] TinyRadii = { 5, 5, 5, 5, 5, 5 };
    private static ICollisionSource EmptySource => new PrimitiveCollisionSource(Array.Empty<CollisionPrimitive>());
    private static CollisionEnvironment Env(double[] radii, double margin = 0, ICollisionSource? source = null, ToolModel? tool = null)
        => new(source ?? EmptySource, radii, tool ?? ToolModel.None, margin);

    // Synthetic robot whose DH chain gives every link a non-zero segment, so
    // self-collision behaviour can be asserted without FR5's zero-length Link2.
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

    [Fact]
    public void SelfCollisionPairs_HasExactly10Entries()
    {
        CollisionDetector.SelfCollisionPairs.Count.Should().Be(10);
    }

    [Fact]
    public void SelfCollisionPairs_ContainsNoAdjacentLinks()
    {
        foreach (var (a, b) in CollisionDetector.SelfCollisionPairs)
        {
            Math.Abs(a - b).Should().BeGreaterThan(1, $"pair ({a},{b}) is adjacent");
            a.Should().BeLessThan(b, $"pair ({a},{b}) not canonically ordered");
        }
    }

    [Fact]
    public void SelfCollisionPairs_CoversAllDocumentedPairs()
    {
        var expected = new HashSet<(int, int)>
        {
            (0,2),(0,3),(0,4),(0,5),
            (1,3),(1,4),(1,5),
            (2,4),(2,5),
            (3,5),
        };
        CollisionDetector.SelfCollisionPairs.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void CheckCollision_NullModel_Throws()
    {
        Action act = () => CollisionDetector.CheckCollision(null!, Joints6<double>.Zero, Env(NormalRadii));
        act.Should().Throw<ArgumentNullException>().And.ParamName.Should().Be("model");
    }

    [Fact]
    public void CheckCollision_NullEnvironment_Throws()
    {
        Action act = () => CollisionDetector.CheckCollision(RobotPresets.FairinoFR5(), Joints6<double>.Zero, null!);
        act.Should().Throw<ArgumentNullException>().And.ParamName.Should().Be("environment");
    }

    [Fact]
    public void CheckCollision_NoCollision_ReturnsEmptyArray_NotNull()
    {
        var result = CollisionDetector.CheckCollision(
            NonDegenerateRobot(), Joints6<double>.Zero, Env(TinyRadii));
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public void CheckCollision_HugeRadii_ReportsAllTenSelfPairs_NoAdjacent()
    {
        // Inflate every link radius so that every pair of non-adjacent capsule axes is closer
        // than r_a + r_b. The arm's longest span at zero joints is well under 1.5 m for FR5,
        // so 5 m radii guarantee every non-adjacent pair violates clearance.
        var fat = new[] { 5000.0, 5000, 5000, 5000, 5000, 5000 };
        var result = CollisionDetector.CheckCollision(
            RobotPresets.FairinoFR5(), Joints6<double>.Zero, Env(fat));

        result.Length.Should().Be(10);

        var reported = result.Select(r => ParsePair(r.BodyA, r.BodyB)).ToHashSet();
        reported.Should().BeEquivalentTo(CollisionDetector.SelfCollisionPairs.ToHashSet());

        foreach (var pair in reported)
        {
            Math.Abs(pair.Item1 - pair.Item2).Should().BeGreaterThan(1);
        }
    }

    [Theory]
    [InlineData(0, 2)][InlineData(0, 3)][InlineData(0, 4)][InlineData(0, 5)]
    [InlineData(1, 3)][InlineData(1, 4)][InlineData(1, 5)]
    [InlineData(2, 4)][InlineData(2, 5)]
    [InlineData(3, 5)]
    public void CheckCollision_EachDocumentedPair_IsReportedWhenRadiiHugeEnough(int a, int b)
    {
        var fat = new[] { 5000.0, 5000, 5000, 5000, 5000, 5000 };
        var result = CollisionDetector.CheckCollision(
            RobotPresets.FairinoFR5(), Joints6<double>.Zero, Env(fat));

        var tag = ($"Link{a + 1}", $"Link{b + 1}");
        result.Select(r => (r.BodyA, r.BodyB)).Should().Contain(tag);
    }

    [Fact]
    public void CheckCollision_TinyRadii_ProducesNoSelfCollisions_OnNonDegenerateRobot()
    {
        var result = CollisionDetector.CheckCollision(
            NonDegenerateRobot(), Joints6<double>.Zero, Env(TinyRadii));
        result.Where(IsSelfPair).Should().BeEmpty();
    }

    [Fact]
    public void CheckCollision_PenetrationDepth_IsPositive_WhenOverlapping()
    {
        var fat = new[] { 5000.0, 5000, 5000, 5000, 5000, 5000 };
        var result = CollisionDetector.CheckCollision(
            RobotPresets.FairinoFR5(), Joints6<double>.Zero, Env(fat));

        foreach (var hit in result.Where(IsSelfPair))
            hit.PenetrationDepth.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CheckCollision_SafetyMargin_PromotesNearMisses_ToHits()
    {
        var baseline = CollisionDetector.CheckCollision(
            NonDegenerateRobot(), Joints6<double>.Zero, Env(TinyRadii));
        baseline.Where(IsSelfPair).Should().BeEmpty();

        var inflated = CollisionDetector.CheckCollision(
            NonDegenerateRobot(), Joints6<double>.Zero, Env(TinyRadii, margin: 10_000));
        inflated.Where(IsSelfPair).Should().NotBeEmpty();
    }

    [Fact]
    public void CheckCollision_EnvironmentHitsAreIncluded()
    {
        // Giant box centred at robot base → every link capsule will penetrate it.
        var box = new BoxPrimitive("Wall", new Point3<double>(0, 0, 0), 10_000, 10_000, 10_000);
        var env = Env(NormalRadii, source: new PrimitiveCollisionSource(box));
        var result = CollisionDetector.CheckCollision(
            RobotPresets.FairinoFR5(), Joints6<double>.Zero, env);

        result.Should().NotBeEmpty();
        result.Any(r => r.BodyB == "Wall" || r.BodyA == "Wall").Should().BeTrue();
    }

    [Fact]
    public void CheckCollision_MixedSelfAndEnvironment_ReportsBoth()
    {
        var fat = new[] { 5000.0, 5000, 5000, 5000, 5000, 5000 };
        var box = new BoxPrimitive("Wall", new Point3<double>(0, 0, 0), 50, 50, 50);
        var env = Env(fat, source: new PrimitiveCollisionSource(box));

        var result = CollisionDetector.CheckCollision(
            RobotPresets.FairinoFR5(), Joints6<double>.Zero, env);

        result.Any(IsSelfPair).Should().BeTrue("self-collision pairs should be reported");
        result.Any(r => r.BodyB == "Wall" || r.BodyA == "Wall").Should().BeTrue("env collision should be reported");
    }

    private static bool IsSelfPair(CollisionResult r) =>
        r.BodyA.StartsWith("Link") && r.BodyB.StartsWith("Link");

    private static (int, int) ParsePair(string a, string b) =>
        (int.Parse(a.AsSpan("Link".Length)) - 1,
         int.Parse(b.AsSpan("Link".Length)) - 1);
}
