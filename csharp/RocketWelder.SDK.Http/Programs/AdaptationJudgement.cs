namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// The latest judgement of one adaptation block in the current or last run, carried by
/// <see cref="ProgramStatus.Adaptations"/> (design.md §12.5). Bands are canonical <c>"accept:review"</c> text in mm.
/// </summary>
/// <param name="BlockId">The adaptation block.</param>
/// <param name="Attempt">The adaptation attempt the judgement belongs to (a re-adapt increments it).</param>
/// <param name="Offset">Measured offset in mm; <see langword="null"/> when not measured.</param>
/// <param name="OffsetBand">The resolved offset band.</param>
/// <param name="OffsetLayer">Where the offset band comes from: <c>block</c>, <c>program</c>, <c>station</c> or <c>builtIn</c>.</param>
/// <param name="Path">Measured path deviation in mm; <see langword="null"/> when not measured.</param>
/// <param name="PathBand">The resolved path band.</param>
/// <param name="PathLayer">Where the path band comes from.</param>
/// <param name="Verdict"><c>accept</c>, <c>review</c> or <c>block</c>.</param>
/// <param name="Trigger">Which quality decided the verdict, e.g. <c>path</c>; <see langword="null"/> when none is named.</param>
public sealed record AdaptationJudgement(
    string BlockId,
    int Attempt,
    double? Offset,
    string OffsetBand,
    string OffsetLayer,
    double? Path,
    string PathBand,
    string PathLayer,
    string Verdict,
    string? Trigger);
