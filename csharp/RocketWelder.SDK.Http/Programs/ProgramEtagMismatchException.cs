namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Thrown when an authoring edit is rejected because the caller's etag is stale —
/// the program was modified since it was read (HTTP 409). Re-read the tree with
/// <see cref="IProgramsApi.GetTreeAsync"/> and retry the edit against the fresh etag.
/// </summary>
public sealed class ProgramEtagMismatchException : Exception
{
    public ProgramEtagMismatchException(Guid programId, ProgramEtag expected)
        : base($"Program '{programId}' was modified since etag '{expected}' was read (HTTP 409). Re-read the tree and retry.")
    {
        ProgramId = programId;
        Expected = expected;
    }

    /// <summary>The program whose edit was rejected.</summary>
    public Guid ProgramId { get; }

    /// <summary>The stale etag the caller supplied in <c>If-Match</c>.</summary>
    public ProgramEtag Expected { get; }
}
