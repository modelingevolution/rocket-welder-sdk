namespace RocketWelder.SDK;

/// <summary>
/// Represents a defined segmentation class in the schema.
/// Returned by <see cref="ISegmentationSchema.DefineClass"/>.
/// </summary>
/// <param name="ClassId">Class ID (matches ML model output)</param>
/// <param name="Name">Human-readable name (e.g., "person", "car")</param>
public readonly record struct SegmentClass(byte ClassId, string Name);
