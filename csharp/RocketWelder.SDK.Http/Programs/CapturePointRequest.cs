namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Body of <c>POST /api/programs/{id}/capture</c> (FR-6). Captures the current pose
/// of rocket-welder2's own <c>IRobot</c> into the program's taught-points store
/// under <see cref="Name"/>, so a subsequently added point block can bake it. This
/// is a saved-store write, never a live read at save time — the point must be
/// captured before the add-point block, or the save refuses to bake <c>(0,0,0)</c>.
/// </summary>
/// <param name="Name">Teaching-point name to write the captured pose under.</param>
/// <param name="RobotName">Optional robot to read; the default (single) robot when omitted.</param>
public sealed record CapturePointRequest(
    string Name,
    string? RobotName = null);
