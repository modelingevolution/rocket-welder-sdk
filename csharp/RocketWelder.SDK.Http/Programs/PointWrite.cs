using System.Text.Json.Serialization;
using RocketWelder.SDK.Http.Robots;

namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// A write of one taught point (epic-128 feature-001 design.md §1.1): an item of <c>PUT /api/programs/{id}/points</c>,
/// and the body of <c>PUT /api/programs/{id}/points/{name}</c> (where rw2 ignores <see cref="Name"/>). A member left
/// <see langword="null"/> is not written to the wire, so rw2 keeps its stored value.
/// </summary>
/// <param name="Name">The declared point to write.</param>
public sealed record PointWrite(string Name)
{
    /// <summary>The new pose (mm, deg, robot base frame); <see langword="null"/> keeps the stored pose. The first write of
    /// a point must carry one.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RobotPose? Pose { get; init; }

    /// <summary>The external-axis samples; <see langword="null"/> keeps the stored ones.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<PointAxis>? Axes { get; init; }

    /// <summary>Six joint values in deg, or empty for "not recorded"; <see langword="null"/> keeps the stored ones.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<double>? Joints { get; init; }

    /// <summary><c>Unknown</c>, <c>Taught</c> or <c>View</c>; <see langword="null"/> keeps the stored origin.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Origin { get; init; }
}
