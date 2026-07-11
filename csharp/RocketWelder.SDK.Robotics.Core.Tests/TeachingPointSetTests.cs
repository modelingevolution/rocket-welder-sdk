using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core.Tests;

/// <summary>TASK-007 — TeachingPointSet CRUD + JSON.</summary>
public class TeachingPointSetTests
{
    private static Pose3<double> Pose(double x) => new(x, 0, 0, 0, 0, 0);

    [Fact]
    public void Set_And_Get_Should_Round_Trip_Value()
    {
        var set = new TeachingPointSet();
        set.Set("home", Pose(100));

        set.Get("home").Should().Be(Pose(100));
        set.Count.Should().Be(1);
    }

    [Fact]
    public void TryGet_Missing_Should_Return_False()
    {
        var set = new TeachingPointSet();
        set.TryGet("nope", out var pose).Should().BeFalse();
        pose.Should().Be(default(Pose3<double>));
    }

    [Fact]
    public void Get_Missing_Should_Throw()
    {
        var set = new TeachingPointSet();
        var act = () => set.Get("nope");
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void Set_Duplicate_Name_Should_Overwrite_Without_Duplicating()
    {
        var set = new TeachingPointSet();
        set.Set("p", Pose(1));
        set.Set("p", Pose(2));

        set.Count.Should().Be(1);
        set.Get("p").Should().Be(Pose(2));
    }

    [Fact]
    public void Remove_Should_Return_True_Then_False()
    {
        var set = new TeachingPointSet();
        set.Set("p", Pose(1));

        set.Remove("p").Should().BeTrue();
        set.Remove("p").Should().BeFalse();
        set.Contains("p").Should().BeFalse();
    }

    [Fact]
    public void Name_Lookup_Is_Case_Sensitive()
    {
        var set = new TeachingPointSet();
        set.Set("Home", Pose(1));

        set.Contains("Home").Should().BeTrue();
        set.Contains("home").Should().BeFalse();
        set.TryGet("home", out _).Should().BeFalse();
    }

    [Fact]
    public void Enumerate_Should_Yield_Insertion_Order()
    {
        var set = new TeachingPointSet();
        set.Set("c", Pose(3));
        set.Set("a", Pose(1));
        set.Set("b", Pose(2));

        set.Enumerate().Select(kv => kv.Key).Should().ContainInOrder("c", "a", "b");
        set.Names.Should().ContainInOrder("c", "a", "b");
    }

    [Fact]
    public void Remove_Then_Readd_Should_Append_At_End()
    {
        var set = new TeachingPointSet();
        set.Set("a", Pose(1));
        set.Set("b", Pose(2));
        set.Remove("a");
        set.Set("a", Pose(9));

        set.Names.Should().ContainInOrder("b", "a");
    }

    [Fact]
    public void JSON_RoundTrip_Should_Preserve_Names_Order_And_Poses()
    {
        var set = new TeachingPointSet();
        set.Set("home", new Pose3<double>(400, 0, 300, 180, 0, 0));
        set.Set("pick", new Pose3<double>(500, 100, 200, 90, 10, -45));
        set.Set("place", new Pose3<double>(-500, 50, 150, 0, 45, 90));

        var json = set.ToJson();
        var restored = TeachingPointSet.FromJson(json);

        restored.Count.Should().Be(3);
        restored.Names.Should().ContainInOrder("home", "pick", "place");
        restored.Get("home").Should().Be(new Pose3<double>(400, 0, 300, 180, 0, 0));
        restored.Get("pick").Should().Be(new Pose3<double>(500, 100, 200, 90, 10, -45));
        restored.Get("place").Should().Be(new Pose3<double>(-500, 50, 150, 0, 45, 90));
    }

    [Fact]
    public void FromJson_Null_Should_Throw()
    {
        var act = () => TeachingPointSet.FromJson(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Set_Null_Name_Should_Throw()
    {
        var set = new TeachingPointSet();
        var act = () => set.Set(null!, Pose(1));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Empty_Set_JSON_RoundTrip()
    {
        var set = new TeachingPointSet();
        var restored = TeachingPointSet.FromJson(set.ToJson());
        restored.Count.Should().Be(0);
    }
}
