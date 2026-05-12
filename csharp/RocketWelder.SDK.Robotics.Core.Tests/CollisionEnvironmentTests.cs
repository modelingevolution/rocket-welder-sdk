using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core.Tests;

public class CollisionEnvironmentTests
{
    private static readonly double[] ValidRadii = { 60, 60, 45, 35, 25, 20 };

    private static ICollisionSource NullSource() => new PrimitiveCollisionSource(Array.Empty<CollisionPrimitive>());

    [Fact]
    public void Ctor_HappyPath_ExposesAllProperties()
    {
        var source = NullSource();
        var tool = ToolModel.None;

        var env = new CollisionEnvironment(source, ValidRadii, tool, safetyMargin: 2.5);

        env.Source.Should().BeSameAs(source);
        env.LinkRadii.Should().Equal(ValidRadii);
        env.Tool.Should().BeSameAs(tool);
        env.SafetyMargin.Should().Be(2.5);
    }

    [Fact]
    public void Ctor_DefaultsSafetyMarginToZero()
    {
        var env = new CollisionEnvironment(NullSource(), ValidRadii, ToolModel.None);
        env.SafetyMargin.Should().Be(0);
    }

    [Fact]
    public void Ctor_NullSource_Throws()
    {
        Action act = () => new CollisionEnvironment(null!, ValidRadii, ToolModel.None);
        act.Should().Throw<ArgumentNullException>().And.ParamName.Should().Be("source");
    }

    [Fact]
    public void Ctor_NullLinkRadii_Throws()
    {
        Action act = () => new CollisionEnvironment(NullSource(), null!, ToolModel.None);
        act.Should().Throw<ArgumentNullException>().And.ParamName.Should().Be("linkRadii");
    }

    [Fact]
    public void Ctor_NullTool_Throws()
    {
        Action act = () => new CollisionEnvironment(NullSource(), ValidRadii, null!);
        act.Should().Throw<ArgumentNullException>().And.ParamName.Should().Be("tool");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(12)]
    public void Ctor_WrongLinkRadiiCount_Throws(int count)
    {
        var radii = Enumerable.Repeat(10.0, count).ToArray();
        Action act = () => new CollisionEnvironment(NullSource(), radii, ToolModel.None);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*6*")
            .And.ParamName.Should().Be("linkRadii");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.0001)]
    public void Ctor_NonPositiveLinkRadius_Throws(double badRadius)
    {
        var radii = new double[] { 50, 50, 50, badRadius, 50, 50 };
        Action act = () => new CollisionEnvironment(NullSource(), radii, ToolModel.None);
        act.Should().Throw<ArgumentException>()
            .And.ParamName.Should().Be("linkRadii");
    }

    [Theory]
    [InlineData(-0.0001)]
    [InlineData(-5)]
    public void Ctor_NegativeSafetyMargin_Throws(double margin)
    {
        Action act = () => new CollisionEnvironment(NullSource(), ValidRadii, ToolModel.None, margin);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .And.ParamName.Should().Be("safetyMargin");
    }

    [Fact]
    public void LinkRadii_IsDefensivelyCopied()
    {
        var mutable = (double[])ValidRadii.Clone();
        var env = new CollisionEnvironment(NullSource(), mutable, ToolModel.None);

        mutable[0] = 999;

        env.LinkRadii[0].Should().Be(60);
    }

    [Fact]
    public void QueryThroughEnvironment_ReachesSource()
    {
        var box = new BoxPrimitive(
            "far",
            new Point3<double>(10_000, 0, 0),
            10, 10, 10);
        var env = new CollisionEnvironment(
            new PrimitiveCollisionSource(new CollisionPrimitive[] { box }),
            ValidRadii,
            ToolModel.None);

        var result = env.Source.QueryCollision(
            RobotPresets.FairinoFR5(),
            Joints6<double>.Zero,
            env.LinkRadii,
            env.Tool,
            env.SafetyMargin);

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }
}
