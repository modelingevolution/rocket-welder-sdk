namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// A program's block tree plus the etag it was read at. The contract every edit is
/// written against: pass <see cref="Etag"/> back in <c>If-Match</c> when editing.
/// </summary>
/// <param name="Blocks">Ordered blocks, top to bottom.</param>
/// <param name="Etag">Concurrency token for the current tree state.</param>
public sealed record ProgramTreeDto(
    IReadOnlyList<BlockDto> Blocks,
    ProgramEtag Etag);
