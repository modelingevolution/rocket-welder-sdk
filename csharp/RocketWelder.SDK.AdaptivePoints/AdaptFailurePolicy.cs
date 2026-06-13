namespace RocketWelder.SDK.AdaptivePoints;

/// <summary>
/// How a program reacts when an adaptive-point fails to resolve (any non-<see cref="AdaptResult.Ok"/>
/// outcome) during <see cref="AdaptContextExtensions.AdaptAsync"/> /
/// <see cref="AdaptContextExtensions.AdaptOffsetAsync"/>. Program-owned: set once with
/// <see cref="AdaptContextExtensions.UseAdaptFailurePolicy"/>; when never set the program runs at the
/// fail-fast default (<see cref="Abort"/>).
/// </summary>
public enum AdaptFailurePolicy
{
    /// <summary>Log a warning, then continue at taught geometry (the corrected pose falls back to the
    /// master taught pose; a datum offset falls back to zero).</summary>
    SkipAndLog,

    /// <summary>Throw <see cref="AdaptationFailedException"/> — stop the program. The default when no
    /// policy is set: a vision failure with no declared policy must not silently weld wrong geometry.</summary>
    Abort,

    /// <summary>Continue at taught geometry silently (corrected pose = master taught pose; datum offset
    /// = zero).</summary>
    FallBackToTaught
}
