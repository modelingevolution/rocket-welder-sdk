using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using ModelingEvolution.Drawing;
using RocketWelder.SDK.Runtime;

namespace RocketWelder.SDK.AdaptivePoints;

/// <summary>
/// One-statement adapt helpers on <see cref="IProgramContext"/>. Each collapses
/// <c>GetAdaptivePoint(name).AdaptAsync(ct)</c> + the <see cref="AdaptResult.Ok"/> unwrap + the
/// program-level failure policy into a single call, so an emitted block maps to one line.
/// </summary>
public static class AdaptContextExtensions
{
    private static readonly ConditionalWeakTable<IProgramContext, object> Policies = new();

    private const AdaptFailurePolicy DefaultPolicy = AdaptFailurePolicy.Abort;

    /// <summary>
    /// Sets the program-owned <see cref="AdaptFailurePolicy"/> for this context. Program-owned: emitted
    /// as the first body statement when the program owns a policy; absent it, the program runs at the
    /// fail-fast default (<see cref="AdaptFailurePolicy.Abort"/>).
    /// </summary>
    public static void UseAdaptFailurePolicy(this IProgramContext ctx, AdaptFailurePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        Policies.AddOrUpdate(ctx, policy);
    }

    /// <summary>
    /// Resolves the corrected pose of the adaptive-point named <paramref name="name"/>, applying the
    /// program-level <see cref="AdaptFailurePolicy"/> on any non-<see cref="AdaptResult.Ok"/> outcome:
    /// <see cref="AdaptFailurePolicy.Abort"/> throws <see cref="AdaptationFailedException"/>;
    /// <see cref="AdaptFailurePolicy.FallBackToTaught"/> and <see cref="AdaptFailurePolicy.SkipAndLog"/>
    /// return the master taught pose (the latter also logs a warning).
    /// </summary>
    /// <exception cref="AdaptationFailedException">The point did not resolve and the policy is
    /// <see cref="AdaptFailurePolicy.Abort"/>.</exception>
    public static async ValueTask<Pose3<double>> AdaptAsync(
        this IProgramContext ctx, string name, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var point = ctx.GetAdaptivePoint(name);
        var result = await point.AdaptAsync(ct).ConfigureAwait(false);
        if (result is AdaptResult.Ok ok)
            return ok.Pose;

        return ctx.GetAdaptFailurePolicy() switch
        {
            AdaptFailurePolicy.FallBackToTaught => point.TaughtPose,
            AdaptFailurePolicy.SkipAndLog => LogAndFallBack(ctx, name, result, point.TaughtPose),
            _ => throw new AdaptationFailedException(name, result)
        };
    }

    /// <summary>
    /// Resolves one reference point's offset (Δ = <see cref="AdaptResult.Ok.Correction"/>) for a
    /// datum-offset Pass — the <see cref="Vector3{T}"/> that shifts every taught pose via the shipped
    /// <c>Pose3 + Vector3</c> operator. Applies the program-level <see cref="AdaptFailurePolicy"/> on any
    /// non-<see cref="AdaptResult.Ok"/> outcome: <see cref="AdaptFailurePolicy.Abort"/> throws;
    /// <see cref="AdaptFailurePolicy.FallBackToTaught"/> and <see cref="AdaptFailurePolicy.SkipAndLog"/>
    /// return a zero offset (taught geometry; the latter also logs a warning).
    /// </summary>
    /// <exception cref="AdaptationFailedException">The datum did not resolve and the policy is
    /// <see cref="AdaptFailurePolicy.Abort"/>.</exception>
    public static async ValueTask<Vector3<double>> AdaptOffsetAsync(
        this IProgramContext ctx, string datumName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var point = ctx.GetAdaptivePoint(datumName);
        var result = await point.AdaptAsync(ct).ConfigureAwait(false);
        if (result is AdaptResult.Ok ok)
            return ok.Correction;

        return ctx.GetAdaptFailurePolicy() switch
        {
            AdaptFailurePolicy.FallBackToTaught => Vector3<double>.Zero,
            AdaptFailurePolicy.SkipAndLog => LogAndFallBack(ctx, datumName, result, Vector3<double>.Zero),
            _ => throw new AdaptationFailedException(datumName, result)
        };
    }

    private static AdaptFailurePolicy GetAdaptFailurePolicy(this IProgramContext ctx)
        => Policies.TryGetValue(ctx, out var boxed) ? (AdaptFailurePolicy)boxed : DefaultPolicy;

    private static T LogAndFallBack<T>(IProgramContext ctx, string name, AdaptResult result, T fallback)
    {
        ctx.Logger.LogWarning(
            "Adaptive-point '{Name}' did not resolve ({Reason}); continuing at taught geometry.",
            name, AdaptationFailedException.Describe(result));
        return fallback;
    }
}
