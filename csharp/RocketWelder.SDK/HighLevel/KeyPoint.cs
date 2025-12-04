namespace RocketWelder.SDK.HighLevel;

/// <summary>
/// Represents a defined keypoint in the schema.
/// Returned by <see cref="IKeyPointsSchema.DefinePoint"/>.
/// </summary>
/// <param name="Id">Auto-assigned sequential ID (0, 1, 2, ...)</param>
/// <param name="Name">Human-readable name (e.g., "nose", "left_eye")</param>
public readonly record struct KeyPoint(int Id, string Name);
