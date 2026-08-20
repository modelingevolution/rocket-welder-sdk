using FluentAssertions;

namespace RocketWelder.SDK.Devices.Motion.Tests;

/// <summary>
/// <b>AC-21</b> — FR-2's central guarantee, verified mechanically: a dimensionally wrong speed or
/// position <b>does not compile</b>. Each case is a pair — the wrong-typed snippet must fail with
/// the compiler's own type error, and its <b>positive twin</b> (the correct types, same shape, same
/// harness) must compile clean. Without the twin a broken harness would make every negative pass.
/// </summary>
public class NegativeCompilationTests
{
    private const string Mm = "Length<double, Millimetre<double>>";
    private const string MmPerS = "Speed<double, MillimetrePerSecond<double>>";
    private const string DegPerS = "AngularSpeed<double, DegreePerSecond<double>>";

    // ---- Case 1: an angular speed handed to a LINEAR axis --------------------

    [Fact]
    public void AngularSpeed_OnLinearAxis_DoesNotCompile()
    {
        var errors = SnippetCompiler.ErrorIds(
            $"        await linear.MoveAbsoluteAsync(new {Mm}(10), new {DegPerS}(5));");

        errors.Should().Contain("CS1503",
            "an angular speed is a different physical dimension from a linear one; " +
            $"got: {SnippetCompiler.Describe($"        await linear.MoveAbsoluteAsync(new {Mm}(10), new {DegPerS}(5));")}");
    }

    [Fact]
    public void Control_LinearSpeed_OnLinearAxis_Compiles()
    {
        var body = $"        await linear.MoveAbsoluteAsync(new {Mm}(10), new {MmPerS}(5));";

        SnippetCompiler.Compile(body).Should().BeEmpty(
            "the harness must still be able to say yes, or the negative twin proves nothing: " +
            SnippetCompiler.Describe(body));
    }

    // ---- Case 2: a millimetre target handed to a ROTARY axis -----------------

    [Fact]
    public void LengthTarget_OnRotaryAxis_DoesNotCompile()
    {
        var body = $"        await rotary.MoveAbsoluteAsync(new {Mm}(10));";

        SnippetCompiler.ErrorIds(body).Should().Contain("CS1503",
            "a rotary axis is positioned in degrees, never in millimetres; got: " + SnippetCompiler.Describe(body));
    }

    [Fact]
    public void Control_DegreeTarget_OnRotaryAxis_Compiles()
    {
        var body = "        await rotary.MoveAbsoluteAsync(Degree<double>.Create(45));";

        SnippetCompiler.Compile(body).Should().BeEmpty(SnippetCompiler.Describe(body));
    }

    [Fact]
    public void Control_BareNumericTarget_OnRotaryAxis_Compiles()
    {
        // Degree<T> converts implicitly from T, so the ergonomic call site architecture.md promises
        // — MoveAbsoluteAsync(45) — really does compile. Typing costs the caller nothing.
        var body = "        await rotary.MoveAbsoluteAsync(45);";

        SnippetCompiler.Compile(body).Should().BeEmpty(SnippetCompiler.Describe(body));
    }

    // ---- Case 3: adding the two speed dimensions -----------------------------

    [Fact]
    public void AngularSpeed_PlusLinearSpeed_DoesNotCompile()
    {
        var body = $"        var x = new {DegPerS}(5) + new {MmPerS}(20);";

        SnippetCompiler.ErrorIds(body).Should().Contain("CS0019",
            "°/s and mm/s have disjoint SI bases (rad/s vs m/s), so the sum is meaningless; got: "
            + SnippetCompiler.Describe(body));
    }

    [Fact]
    public void Control_AngularSpeed_PlusAngularSpeed_Compiles()
    {
        var body = $"        var x = new {DegPerS}(5) + new {DegPerS}(2);";

        SnippetCompiler.Compile(body).Should().BeEmpty(SnippetCompiler.Describe(body));
    }

    // ---- The mirrors of cases 1 and 2, for symmetry --------------------------

    [Fact]
    public void LinearSpeed_OnRotaryAxis_DoesNotCompile()
    {
        var body = $"        await rotary.MoveAbsoluteAsync(45, new {MmPerS}(5));";

        SnippetCompiler.ErrorIds(body).Should().Contain("CS1503", SnippetCompiler.Describe(body));
    }

    [Fact]
    public void DegreeTarget_OnLinearAxis_DoesNotCompile()
    {
        var body = "        await linear.MoveAbsoluteAsync(Degree<double>.Create(45));";

        SnippetCompiler.ErrorIds(body).Should().Contain("CS1503", SnippetCompiler.Describe(body));
    }

    // ---- P-2: MoveVelocity has no Percentage overload ------------------------

    [Fact]
    public void PercentageVelocity_DoesNotCompile()
    {
        // A Percentage cannot be signed, and velocity carries its direction in the sign (P-2).
        var body = "        await rotary.MoveVelocityAsync(new Percentage(20));";

        SnippetCompiler.ErrorIds(body).Should().Contain("CS1503", SnippetCompiler.Describe(body));
    }

    [Fact]
    public void Control_SignedTypedVelocity_Compiles()
    {
        var body = $"        await rotary.MoveVelocityAsync(new {DegPerS}(-5));";

        SnippetCompiler.Compile(body).Should().BeEmpty(SnippetCompiler.Describe(body));
    }

    [Fact]
    public void Control_PercentageOfMax_OnPositioningMove_Compiles()
    {
        var body = "        await rotary.MoveAbsoluteAsync(45, new Percentage(20));";

        SnippetCompiler.Compile(body).Should().BeEmpty(SnippetCompiler.Describe(body));
    }

    // ---- The harness itself --------------------------------------------------

    [Fact]
    public void Control_EmptyBody_Compiles()
    {
        // The floor: if this fails, every "does not compile" result above is an artefact of the
        // harness (a missing reference, a bad preamble), not of the contract.
        SnippetCompiler.Compile("        await Task.CompletedTask;").Should().BeEmpty(
            SnippetCompiler.Describe("        await Task.CompletedTask;"));
    }

    [Fact]
    public void Control_HarnessReportsAnOrdinaryError()
    {
        // ...and if this passes, the harness is genuinely reading diagnostics rather than
        // returning an empty list for everything.
        SnippetCompiler.ErrorIds("        int x = \"not an int\";").Should().Contain("CS0029");
    }
}
