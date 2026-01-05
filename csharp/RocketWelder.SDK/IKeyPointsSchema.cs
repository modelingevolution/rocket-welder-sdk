using System.Collections.Generic;

namespace RocketWelder.SDK;

/// <summary>
/// Schema for defining keypoints. Static, defined once at startup.
/// </summary>
public interface IKeyPointsSchema
{
    /// <summary>
    /// Defines a keypoint with a human-readable name.
    /// ID is auto-assigned sequentially (0, 1, 2, ...).
    /// </summary>
    /// <param name="name">Human-readable name (e.g., "nose", "left_eye")</param>
    /// <returns>KeyPointDefinition struct for use in data contexts</returns>
    KeyPointDefinition DefinePoint(string name);

    /// <summary>
    /// Gets all defined keypoints.
    /// </summary>
    IReadOnlyList<KeyPointDefinition> DefinedPoints { get; }

    /// <summary>
    /// Gets metadata as JSON for readers/consumers.
    /// </summary>
    string GetMetadataJson();
}
