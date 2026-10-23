using RocketWelder.SDK.Http.Robots;

namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// One taught point of a program (<c>GET /api/programs/{id}/points[/{name}]</c>, epic-128 feature-001 design.md §1.1).
/// rw2 returns this type (ADR-1).
/// </summary>
/// <param name="Name">The point name, as declared by the program's Point block.</param>
/// <param name="Pose">x, y, z in mm and rx, ry, rz in deg, robot base frame.</param>
/// <param name="CapturedAt">When the point was last written (UTC).</param>
/// <param name="Origin"><c>Unknown</c>, <c>Taught</c> or <c>View</c>.</param>
/// <param name="Axes">The external-axis samples; empty when none were recorded.</param>
/// <param name="Joints">Six joint values in deg (j1..j6), or empty when not recorded.</param>
public sealed record ProgramPoint(
    string Name,
    RobotPose Pose,
    DateTimeOffset CapturedAt,
    string Origin,
    IReadOnlyList<PointAxis> Axes,
    IReadOnlyList<double> Joints);
