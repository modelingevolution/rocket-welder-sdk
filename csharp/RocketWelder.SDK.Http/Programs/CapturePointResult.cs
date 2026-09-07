namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Result of <c>POST /api/programs/{id}/capture</c> — the teaching point that was written.
/// </summary>
/// <param name="Name">The teaching-point name the captured pose was stored under.</param>
public sealed record CapturePointResult(string Name);
