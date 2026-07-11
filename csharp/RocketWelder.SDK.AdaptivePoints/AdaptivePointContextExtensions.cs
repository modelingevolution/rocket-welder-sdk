using RocketWelder.SDK.Runtime;

namespace RocketWelder.SDK.AdaptivePoints;

/// <summary>
/// Extension methods on <see cref="IProgramContext"/> that surface adaptive-point lookup.
/// Mirrors <c>GetRequiredDevice&lt;T&gt;</c>'s "required" shape — the generated program code
/// calls <c>ctx.GetAdaptivePoint(name).AdaptAsync(ct)</c> without a null check, so a missing
/// name is a hard error.
/// </summary>
public static class AdaptivePointContextExtensions
{
    /// <summary>
    /// Returns the live adaptive-point handle named <paramref name="name"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No <see cref="IAdaptivePointService"/> is registered (thrown by
    /// <see cref="IProgramContext.GetRequiredDevice{T}(string?)"/> — adaptive-points are
    /// robot-scoped, so the service is only present once a robot is bound), or no
    /// adaptive-point named <paramref name="name"/> exists for this program's active robot.
    /// </exception>
    public static IAdaptivePoint GetAdaptivePoint(this IProgramContext ctx, string name)
        => ctx.GetRequiredDevice<IAdaptivePointService>().Get(name)
            ?? throw new InvalidOperationException(
                $"Adaptive-point '{name}' not found in this robot's catalogue.");
}
