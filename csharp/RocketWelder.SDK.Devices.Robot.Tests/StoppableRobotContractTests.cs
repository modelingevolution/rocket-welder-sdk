using System.Reflection;

namespace RocketWelder.SDK.Devices.Robot.Tests;

/// <summary>
/// Pins the shape of <see cref="IStoppableRobot"/>, published in 2.18.0 (epic-092 design §3.6 step 1) so a
/// plugin can drive a robot's emergency stop without referencing the host. It was an internal host interface
/// before, and every host implementer (FairinoCobot, SimulatorRobot) now binds to THIS declaration — a rename
/// or an added member is a breaking change and must be deliberate.
///
/// <para>The "no vendor name" test is the SDK half of NFR-1 / RES-7 (test-scenarios §6): the source is
/// embedded by the test csproj, so the test cannot pass by reading the wrong file.</para>
/// </summary>
public sealed class StoppableRobotContractTests
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    [Fact]
    public void IStoppableRobot_DeclaresExactlyStopAndBeginRun()
    {
        var methods = typeof(IStoppableRobot).GetMethods(Declared)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        methods.Should().Equal("BeginRun", "Stop");
        typeof(IStoppableRobot).GetProperties(Declared).Should().BeEmpty();
        typeof(IStoppableRobot).GetEvents(Declared).Should().BeEmpty();
    }

    [Fact]
    public void Stop_IsAParameterlessVoid()
    {
        var stop = typeof(IStoppableRobot).GetMethod("Stop", Declared)!;

        stop.GetParameters().Should().BeEmpty();
        stop.ReturnType.Should().Be(typeof(void));
    }

    [Fact]
    public void BeginRun_TakesACancellationToken_AndReturnsTheRunScope()
    {
        var beginRun = typeof(IStoppableRobot).GetMethod("BeginRun", Declared)!;

        beginRun.GetParameters().Select(p => p.ParameterType).Should().Equal(typeof(CancellationToken));
        beginRun.ReturnType.Should().Be(typeof(IDisposable));
    }

    [Fact]
    public void IStoppableRobot_IsNotAnIRobot()
    {
        // Design §3.6: not every arm can be halted. A host must be able to ask, so the two stay separate.
        typeof(IRobot).IsAssignableFrom(typeof(IStoppableRobot)).Should().BeFalse();
    }

    [Fact]
    public void TheSourceNamesNoVendor()
    {
        var asm = typeof(StoppableRobotContractTests).Assembly;
        var name = asm.GetManifestResourceNames().Single(n => n.EndsWith(".IStoppableRobot.cs", StringComparison.Ordinal));
        using var s = asm.GetManifestResourceStream(name)!;
        new StreamReader(s).ReadToEnd()
            .Should().NotContainEquivalentOf("fairino",
                "NFR-1: the published stop seam is vendor-neutral (test-scenarios §6 RES-7)");
    }
}
