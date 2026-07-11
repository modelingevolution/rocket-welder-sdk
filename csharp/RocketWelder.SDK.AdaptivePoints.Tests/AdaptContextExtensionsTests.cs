using Microsoft.Extensions.Logging;
using ModelingEvolution.Drawing;
using NSubstitute;
using RocketWelder.SDK.Runtime;

namespace RocketWelder.SDK.AdaptivePoints.Tests;

public class AdaptContextExtensionsTests
{
    private const string PointName = "A";
    private static readonly Pose3<double> Taught = new(1, 2, 3, 10, 20, 30);
    private static readonly Pose3<double> Corrected = new(4, 5, 6, 11, 21, 31);
    private static readonly Vector3<double> Correction = Vector3<double>.From(0.1, 0.2, 0.3);

    private sealed class Harness
    {
        public required IProgramContext Ctx { get; init; }
        public required IAdaptivePoint Point { get; init; }
        public required ILogger Logger { get; init; }
    }

    private static Harness Build(AdaptResult result)
    {
        var point = Substitute.For<IAdaptivePoint>();
        point.TaughtPose.Returns(Taught);
        point.AdaptAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(result));

        var service = Substitute.For<IAdaptivePointService>();
        service.Get(PointName).Returns(point);

        var logger = Substitute.For<ILogger>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        var ctx = Substitute.For<IProgramContext>();
        ctx.GetRequiredDevice<IAdaptivePointService>().Returns(service);
        ctx.Logger.Returns(logger);

        return new Harness { Ctx = ctx, Point = point, Logger = logger };
    }

    private static void AssertWarningLogged(ILogger logger) =>
        logger.ReceivedWithAnyArgs(1).Log<Arg.AnyType>(default, default, default!, default, default!);

    private static void AssertNothingLogged(ILogger logger) =>
        logger.DidNotReceiveWithAnyArgs().Log<Arg.AnyType>(default, default, default!, default, default!);

    // === AdaptAsync ===

    [Fact]
    public async Task AdaptAsync_Should_Return_Corrected_Pose_On_Ok()
    {
        // Arrange
        var h = Build(new AdaptResult.Ok(Corrected, Correction));

        // Act
        var pose = await h.Ctx.AdaptAsync(PointName, default);

        // Assert
        pose.Should().Be(Corrected);
        AssertNothingLogged(h.Logger);
    }

    [Fact]
    public async Task AdaptAsync_Should_Return_Corrected_Pose_On_Ok_Even_Under_Abort()
    {
        // Arrange
        var h = Build(new AdaptResult.Ok(Corrected, Correction));
        h.Ctx.UseAdaptFailurePolicy(AdaptFailurePolicy.Abort);

        // Act
        var pose = await h.Ctx.AdaptAsync(PointName, default);

        // Assert
        pose.Should().Be(Corrected);
    }

    [Fact]
    public async Task AdaptAsync_Should_Throw_On_Failure_With_Default_Policy()
    {
        // Arrange — no UseAdaptFailurePolicy: default is fail-fast Abort
        var h = Build(new AdaptResult.NoFrame());

        // Act
        var act = async () => await h.Ctx.AdaptAsync(PointName, default);

        // Assert
        await act.Should().ThrowAsync<AdaptationFailedException>();
    }

    [Fact]
    public async Task AdaptAsync_Should_Throw_With_Point_And_Result_Under_Abort()
    {
        // Arrange
        var result = new AdaptResult.NoDetection();
        var h = Build(result);
        h.Ctx.UseAdaptFailurePolicy(AdaptFailurePolicy.Abort);

        // Act
        var act = async () => await h.Ctx.AdaptAsync(PointName, default);

        // Assert
        var ex = (await act.Should().ThrowAsync<AdaptationFailedException>()).Which;
        ex.PointName.Should().Be(PointName);
        ex.Result.Should().BeSameAs(result);
    }

    [Fact]
    public async Task AdaptAsync_Should_Return_Taught_Pose_Under_FallBackToTaught()
    {
        // Arrange
        var h = Build(new AdaptResult.Stale("recalibrated"));
        h.Ctx.UseAdaptFailurePolicy(AdaptFailurePolicy.FallBackToTaught);
        h.Logger.ClearReceivedCalls();

        // Act
        var pose = await h.Ctx.AdaptAsync(PointName, default);

        // Assert
        pose.Should().Be(Taught);
        AssertNothingLogged(h.Logger);
    }

    [Fact]
    public async Task AdaptAsync_Should_Return_Taught_Pose_And_Log_Under_SkipAndLog()
    {
        // Arrange
        var h = Build(new AdaptResult.OutOfRange(Correction));
        h.Ctx.UseAdaptFailurePolicy(AdaptFailurePolicy.SkipAndLog);
        h.Logger.ClearReceivedCalls();

        // Act
        var pose = await h.Ctx.AdaptAsync(PointName, default);

        // Assert
        pose.Should().Be(Taught);
        AssertWarningLogged(h.Logger);
    }

    // === AdaptOffsetAsync ===

    [Fact]
    public async Task AdaptOffsetAsync_Should_Return_Correction_On_Ok()
    {
        // Arrange
        var h = Build(new AdaptResult.Ok(Corrected, Correction));

        // Act
        var offset = await h.Ctx.AdaptOffsetAsync(PointName, default);

        // Assert
        offset.Should().Be(Correction);
    }

    [Fact]
    public async Task AdaptOffsetAsync_Should_Throw_On_Failure_With_Default_Policy()
    {
        // Arrange
        var h = Build(new AdaptResult.NoFrame());

        // Act
        var act = async () => await h.Ctx.AdaptOffsetAsync(PointName, default);

        // Assert
        await act.Should().ThrowAsync<AdaptationFailedException>();
    }

    [Fact]
    public async Task AdaptOffsetAsync_Should_Return_Zero_Under_FallBackToTaught()
    {
        // Arrange
        var h = Build(new AdaptResult.NoDetection());
        h.Ctx.UseAdaptFailurePolicy(AdaptFailurePolicy.FallBackToTaught);
        h.Logger.ClearReceivedCalls();

        // Act
        var offset = await h.Ctx.AdaptOffsetAsync(PointName, default);

        // Assert
        offset.Should().Be(Vector3<double>.Zero);
        AssertNothingLogged(h.Logger);
    }

    [Fact]
    public async Task AdaptOffsetAsync_Should_Return_Zero_And_Log_Under_SkipAndLog()
    {
        // Arrange
        var h = Build(new AdaptResult.Stale("drift"));
        h.Ctx.UseAdaptFailurePolicy(AdaptFailurePolicy.SkipAndLog);
        h.Logger.ClearReceivedCalls();

        // Act
        var offset = await h.Ctx.AdaptOffsetAsync(PointName, default);

        // Assert
        offset.Should().Be(Vector3<double>.Zero);
        AssertWarningLogged(h.Logger);
    }

    // === UseAdaptFailurePolicy ===

    [Fact]
    public async Task UseAdaptFailurePolicy_Should_Be_Per_Context()
    {
        // Arrange — two independent contexts with opposite policies
        var aborting = Build(new AdaptResult.NoFrame());
        aborting.Ctx.UseAdaptFailurePolicy(AdaptFailurePolicy.Abort);

        var fallingBack = Build(new AdaptResult.NoFrame());
        fallingBack.Ctx.UseAdaptFailurePolicy(AdaptFailurePolicy.FallBackToTaught);

        // Act
        var abortAct = async () => await aborting.Ctx.AdaptAsync(PointName, default);
        var pose = await fallingBack.Ctx.AdaptAsync(PointName, default);

        // Assert
        await abortAct.Should().ThrowAsync<AdaptationFailedException>();
        pose.Should().Be(Taught);
    }

    [Fact]
    public async Task AdaptAsync_Should_Throw_On_Null_Context()
    {
        // Arrange
        IProgramContext ctx = null!;

        // Act
        var act = async () => await ctx.AdaptAsync(PointName, default);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task AdaptOffsetAsync_Should_Throw_On_Null_Context()
    {
        // Arrange
        IProgramContext ctx = null!;

        // Act
        var act = async () => await ctx.AdaptOffsetAsync(PointName, default);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void UseAdaptFailurePolicy_Should_Throw_On_Null_Context()
    {
        // Arrange
        IProgramContext ctx = null!;

        // Act
        var act = () => ctx.UseAdaptFailurePolicy(AdaptFailurePolicy.Abort);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
