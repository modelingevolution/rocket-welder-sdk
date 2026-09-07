namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Body of <c>POST /api/programs/{id}/capture</c> (FR-6). Captures the current pose
/// of rocket-welder2's own <c>IRobot</c> into the program's taught-points store, so a
/// subsequently added point block can bake it. This is a saved-store write, never a
/// live read at save time — the point must be captured before the add-point block, or
/// the save refuses to bake <c>(0,0,0)</c>.
/// </summary>
/// <param name="Name">
/// Teaching-point name to write the captured pose under. Optional — the server mints
/// the next name (<c>t1</c>, <c>t2</c>, …) when omitted; the chosen name comes back in
/// <see cref="CapturePointResult"/>.
/// </param>
public sealed record CapturePointRequest(string? Name = null);
