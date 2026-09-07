using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelingEvolution.Drawing;
using ModelingEvolution.Signals;
using RocketWelder.SDK.Hmi;
using RocketWelder.SDK.Runtime;
using Xunit;

namespace RocketWelder.SDK.Tests;

/// <summary>
/// Epic 091 — the <see cref="IProgramContext.Signal"/> default: what a program gets on a host that does not
/// publish program signals. It must be a working sink (writes latch, nothing throws), idempotent per name
/// (a program may declare inside its loop — FR-2), validated exactly like a publishing host would (FR-4), and
/// private to the context instance.
/// </summary>
public class ProgramSignalTests
{
    /// <summary>A context that overrides nothing, so every call reaches the interface default.</summary>
    private sealed class DefaultContext : IProgramContext
    {
        public IKeyPointsProvider Keypoints => throw new NotSupportedException();
        public ISegmentationProvider Segmentation => throw new NotSupportedException();
        public ILogger Logger => NullLogger.Instance;
        public IUiSink Ui => throw new NotSupportedException();
        public IActionsStore Actions => throw new NotSupportedException();
        public bool IsDryRun => false;
        public T? GetDevice<T>(string? name = null) where T : class => null;
        public T GetRequiredDevice<T>(string? name = null) where T : class => throw new InvalidOperationException();
        public T? GetById<T>(uint id) where T : class => null;
    }

    [Fact]
    public void The_default_channel_is_a_working_but_unregistered_sink()
    {
        IProgramContext ctx = new DefaultContext();

        var sink = ctx.Signal("centroid-x", "px", Frequency<float>.FromHertz(50));
        sink.Set(1234.5f);

        var signal = Assert.IsAssignableFrom<ISignal<float>>(sink);
        Assert.True(signal.HasValue);
        Assert.Equal(1234.5f, signal.Value);
        Assert.Equal("centroid-x", sink.Metadata.Name);
        Assert.Equal("px", sink.Metadata.Unit);
        Assert.Equal(new Uri("program://local/centroid-x"), sink.Metadata.Uri);
    }

    [Fact]
    public void Declaring_the_same_name_again_returns_the_same_sink()
    {
        IProgramContext ctx = new DefaultContext();

        var first = ctx.Signal("gap", "mm");
        for (var i = 0; i < 1000; i++)
            Assert.Same(first, ctx.Signal("gap", "mm"));
    }

    [Fact]
    public void Channels_are_private_to_the_context_instance()
    {
        IProgramContext a = new DefaultContext();
        IProgramContext b = new DefaultContext();

        Assert.NotSame(a.Signal("gap"), b.Signal("gap"));
    }

    [Theory]
    [InlineData("seam gap?raw")]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("-leading-dash")]
    [InlineData("a/b")]
    [InlineData("übergang")]
    public void An_unaddressable_name_is_refused_at_declaration(string name)
    {
        IProgramContext ctx = new DefaultContext();

        var ex = Assert.Throws<ArgumentException>(() => ctx.Signal(name));
        Assert.Equal("name", ex.ParamName);
    }

    [Fact]
    public void A_name_longer_than_64_characters_is_refused_and_says_so()
    {
        var ex = Assert.Throws<ArgumentException>(() => ProgramSignalName.Validate(new string('a', 65)));
        Assert.Contains("65", ex.Message);
        Assert.Contains("64", ex.Message);
    }

    [Fact]
    public void The_refusal_names_the_offending_characters()
    {
        var ex = Assert.Throws<ArgumentException>(() => ProgramSignalName.Validate("seam gap?raw"));
        Assert.Contains("' '", ex.Message);
        Assert.Contains("'?'", ex.Message);
    }

    [Theory]
    [InlineData("centroid-x")]
    [InlineData("a")]
    [InlineData("x.y_z~1")]
    [InlineData("A0")]
    public void Addressable_names_pass(string name)
    {
        Assert.True(ProgramSignalName.IsValid(name));
        Assert.True(ProgramSignalName.IsValid(new string('z', 64)));
    }
}
