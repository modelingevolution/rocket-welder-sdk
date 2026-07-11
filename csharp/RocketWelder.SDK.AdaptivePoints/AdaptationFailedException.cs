namespace RocketWelder.SDK.AdaptivePoints;

/// <summary>
/// Thrown by <see cref="AdaptContextExtensions.AdaptAsync"/> /
/// <see cref="AdaptContextExtensions.AdaptOffsetAsync"/> under <see cref="AdaptFailurePolicy.Abort"/>
/// when an adaptive-point resolves to any non-<see cref="AdaptResult.Ok"/> outcome.
/// </summary>
public sealed class AdaptationFailedException : Exception
{
    /// <summary>The adaptive-point that failed to resolve.</summary>
    public string PointName { get; }

    /// <summary>The non-Ok outcome that triggered the abort.</summary>
    public AdaptResult Result { get; }

    /// <summary>Creates the exception for the adaptive-point <paramref name="pointName"/> that resolved
    /// to the non-Ok <paramref name="result"/>.</summary>
    public AdaptationFailedException(string pointName, AdaptResult result)
        : base($"Adaptive-point '{pointName}' did not resolve: {Describe(result)}.")
    {
        PointName = pointName;
        Result = result;
    }

    internal static string Describe(AdaptResult result) => result switch
    {
        AdaptResult.Ok => "succeeded",
        AdaptResult.Stale s => $"calibration is stale ({s.Reason})",
        AdaptResult.NoFrame => "camera returned no frame",
        AdaptResult.NoDetection => "no feature detected",
        AdaptResult.OutOfRange o => $"correction out of range (attempted {o.Attempted})",
        _ => "unknown failure"
    };
}
