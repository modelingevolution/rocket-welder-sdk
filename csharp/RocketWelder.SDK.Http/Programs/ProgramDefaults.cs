namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// A program's default values (<c>/api/programs/{id}/defaults</c>, design.md §12.3). Defaults are C# statics in the
/// program's companion partial: writing one recompiles the program, so writes carry <c>If-Match</c> and return the new
/// etag.
/// </summary>
/// <param name="Etag">The program's etag after the read or write — carry it into the next edit.</param>
/// <param name="Values">Every program-level value key.</param>
public sealed record ProgramDefaults(ProgramEtag Etag, IReadOnlyList<ProgramDefault> Values)
{
    /// <summary>rw2's own text when the write landed but its history entry did not; <see langword="null"/> otherwise.</summary>
    public string? HistoryWarning { get; init; }
}
