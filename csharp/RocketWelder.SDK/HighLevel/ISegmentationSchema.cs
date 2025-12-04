using System.Collections.Generic;

namespace RocketWelder.SDK.HighLevel;

/// <summary>
/// Schema for defining segmentation classes. Static, defined once at startup.
/// </summary>
public interface ISegmentationSchema
{
    /// <summary>
    /// Defines a segmentation class with explicit ID and name.
    /// </summary>
    /// <param name="classId">Class ID (matches ML model output)</param>
    /// <param name="name">Human-readable name (e.g., "person", "car")</param>
    /// <returns>SegmentClass struct for use in data contexts</returns>
    SegmentClass DefineClass(byte classId, string name);

    /// <summary>
    /// Gets all defined classes.
    /// </summary>
    IReadOnlyList<SegmentClass> DefinedClasses { get; }

    /// <summary>
    /// Gets metadata as JSON for readers/consumers.
    /// </summary>
    string GetMetadataJson();
}
