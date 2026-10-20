namespace RocketWelder.SDK.Abstractions.Values;

/// <summary>
/// What a <see cref="ToleranceBand"/> says about a measured deviation (epic-029 feature-011, design.md §2, FR-1.3).
/// Ordered from best to worst, so the worse of two verdicts is the larger one.
/// </summary>
public enum Verdict
{
    /// <summary>The correction is applied with no operator click.</summary>
    Accept = 0,

    /// <summary>The operator reviews the correction before it is applied.</summary>
    Review = 1,

    /// <summary>The run cannot proceed with this adaptation.</summary>
    Block = 2,
}
