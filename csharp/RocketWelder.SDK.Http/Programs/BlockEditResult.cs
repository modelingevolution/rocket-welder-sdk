namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Result of an add/edit edit — the affected block plus the tree's new etag to
/// carry into the next edit.
/// </summary>
/// <param name="Block">The block that was added or edited.</param>
/// <param name="Etag">The tree's etag after the edit.</param>
public sealed record BlockEditResult(
    BlockId Block,
    ProgramEtag Etag);
