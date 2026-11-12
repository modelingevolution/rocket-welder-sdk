namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Result of <c>POST /api/programs/{programId}/undo</c> — the designer's Undo: the line of the step that was undone and the
/// tree's new etag to carry into the next edit.
/// </summary>
/// <param name="Undone">The history line of the undone step; <see langword="null"/> when rw2 names none.</param>
/// <param name="Etag">The tree's etag after the undo.</param>
public sealed record UndoResult(
    string? Undone,
    ProgramEtag Etag);
