using RocketWelder.SDK.Runtime;

namespace RocketWelder.SDK.AdaptivePoints;

/// <summary>
/// Extension methods on <see cref="IProgramContext"/> that surface taught-path lookup. Twin to
/// <see cref="AdaptivePointContextExtensions.GetAdaptivePoint"/> — the generated program code calls
/// <c>ctx.GetAdaptivePath(startName).TraverseAsync(velocity, ct)</c> without a null check, so a
/// missing name is a hard error.
/// </summary>
public static class AdaptivePathContextExtensions
{
    /// <summary>
    /// Returns the live path handle whose start point is named <paramref name="startName"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No <see cref="IAdaptivePathService"/> is registered (thrown by
    /// <see cref="IProgramContext.GetRequiredDevice{T}(string?)"/> — paths are robot-scoped, so the
    /// service is only present once a robot is bound), or no path starts at
    /// <paramref name="startName"/> for this program's active robot.
    /// </exception>
    public static IAdaptivePath GetAdaptivePath(this IProgramContext ctx, string startName)
        => ctx.GetRequiredDevice<IAdaptivePathService>().Get(startName)
            ?? throw new InvalidOperationException(
                $"Adaptive-path starting at '{startName}' not found in this robot's catalogue.");
}
