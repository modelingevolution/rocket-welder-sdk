namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Result of <c>POST /api/programs/{id}/capture</c> — the teaching point that was written.
/// </summary>
/// <param name="Name">The teaching-point name the captured pose was stored under.</param>
public sealed record CapturePointResult(string Name)
{
    /// <summary>rw2's own text when the pose was saved but its history entry was not
    /// (<c>X-History-Not-Recorded</c>); <see langword="null"/> when the capture was recorded.</summary>
    public string? HistoryWarning { get; init; }
}
