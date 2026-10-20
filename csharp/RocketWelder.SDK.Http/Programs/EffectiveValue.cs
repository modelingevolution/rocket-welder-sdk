namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// One value key of a block or of a program's defaults, with what is stored at that level and what the run resolves
/// (epic-029 feature-011, design.md §12.2–§12.3). Bands travel as canonical <c>"accept:review"</c> text in mm.
/// </summary>
/// <param name="Key">The value key, e.g. <c>offsetTolerance</c>, <c>pathTolerance</c>.</param>
/// <param name="Stored">The raw text stored at this level; <see langword="null"/> when this level inherits.</param>
/// <param name="Effective">The value the run uses, canonical text.</param>
/// <param name="Layer">Where <paramref name="Effective"/> comes from: <c>block</c>, <c>program</c>, <c>robot</c> or <c>builtIn</c>.</param>
/// <param name="Warning">The refusal sentence when <paramref name="Stored"/> is unreadable and the level inherits; otherwise <see langword="null"/>.</param>
public sealed record EffectiveValue(string Key, string? Stored, string Effective, string Layer, string? Warning);
