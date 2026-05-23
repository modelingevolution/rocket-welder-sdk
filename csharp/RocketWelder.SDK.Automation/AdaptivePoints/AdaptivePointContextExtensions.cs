namespace RocketWelder.SDK.Automation.AdaptivePoints;

/// <summary>
/// Extension methods on <see cref="IProgramContext"/> that surface adaptive-point lookup.
/// Mirrors <c>GetRequiredDevice&lt;T&gt;</c>'s "required" shape — the generated program code
/// calls <c>ctx.GetAdaptivePoint(name).AdaptAsync(ct)</c> without a null check, so a missing
/// service OR a missing name is a hard error.
///
/// <para>
/// Per FR-1.1, the <see cref="IAdaptivePointService"/> is resolved via the generic
/// <see cref="IProgramContext.GetService{T}"/> rather than a dedicated property — keeps the
/// <see cref="IProgramContext"/> surface lean (one property per future SDK service does not
/// scale).
/// </para>
/// </summary>
public static class AdaptivePointContextExtensions
{
    /// <summary>
    /// Returns the live adaptive-point handle named <paramref name="name"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The host did not register an <see cref="IAdaptivePointService"/> (likely no
    /// <see cref="IRobot"/> is bound), or no adaptive-point named <paramref name="name"/>
    /// exists for this program's active robot.
    /// </exception>
    public static IAdaptivePoint GetAdaptivePoint(this IProgramContext ctx, string name)
    {
        var svc = ctx.GetService<IAdaptivePointService>()
            ?? throw new InvalidOperationException(
                "Adaptive-points service not registered — the host needs to bind an " +
                "IAdaptivePointService (typically requires an IRobot to be registered).");
        return svc.Get(name)
            ?? throw new InvalidOperationException(
                $"Adaptive-point '{name}' not found in this robot's catalogue.");
    }
}
