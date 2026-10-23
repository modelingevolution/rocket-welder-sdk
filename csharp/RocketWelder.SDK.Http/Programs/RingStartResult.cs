using RocketWelder.SDK.Http.Robots;

namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Result of <c>POST /api/programs/{id}/blocks/{blockId}/calculate-start</c> (epic-128 feature-001 design.md §1.1): the
/// ring's start point as written (<c>circle_&lt;blockId&gt;_start</c>) and the program's etag after the save.
/// </summary>
/// <param name="Name">The point the start pose was written under.</param>
/// <param name="Pose">The computed start pose (mm, deg, robot base frame).</param>
/// <param name="Etag">The program's etag after the edit — carry it into the next edit.</param>
public sealed record RingStartResult(string Name, RobotPose Pose, ProgramEtag Etag)
{
    /// <summary>rw2's own text when the write landed but its history entry did not (<c>X-History-Not-Recorded</c>);
    /// <see langword="null"/> when the write was recorded.</summary>
    public string? HistoryWarning { get; init; }
}
