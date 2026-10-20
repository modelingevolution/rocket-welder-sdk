namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// One program default (<c>/api/programs/{id}/defaults</c>, epic-029 feature-011, design.md §12.3, ADR-28). The program
/// holds a copy of the station defaults; the run resolves block → program → built-in. Bands are canonical
/// <c>"accept:review"</c> text in mm.
/// </summary>
/// <param name="Key">The index key, e.g. <c>Adaptation.OffsetTolerance</c>.</param>
/// <param name="Stored">The program's static; <see langword="null"/> when the key was never copied into the program.</param>
/// <param name="Effective">The value the run uses: <paramref name="Stored"/>, else the built-in.</param>
/// <param name="Layer">Where <paramref name="Effective"/> comes from: <c>program</c> or <c>builtIn</c>.</param>
/// <param name="Station">The station's current effective value for the key (what a reset would copy).</param>
/// <param name="Copied">Whether the key is in the program's statics (false for every key of a program created before
/// this feature).</param>
/// <param name="Warning">The refusal sentence when <paramref name="Stored"/> is unreadable; otherwise <see langword="null"/>.</param>
public sealed record ProgramDefault(
    string Key,
    string? Stored,
    string Effective,
    string Layer,
    string Station,
    bool Copied,
    string? Warning);
