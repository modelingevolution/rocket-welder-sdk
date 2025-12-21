using System.Drawing;

namespace RocketWelder.BinaryProtocol;

/// <summary>
/// Represents a single segmentation instance (object mask) in a frame.
/// Contains the class, instance ID, and polygon points defining the mask boundary.
/// </summary>
public readonly struct SegmentationInstance
{
    /// <summary>
    /// Class identifier (e.g., 0=person, 1=car, etc.)
    /// </summary>
    public byte ClassId { get; init; }

    /// <summary>
    /// Instance identifier within the class (for distinguishing multiple objects of same class).
    /// </summary>
    public byte InstanceId { get; init; }

    /// <summary>
    /// Polygon points defining the segmentation mask boundary.
    /// Points are in pixel coordinates.
    /// </summary>
    public Point[] Points { get; init; }

    public SegmentationInstance(byte classId, byte instanceId, Point[] points)
    {
        ClassId = classId;
        InstanceId = instanceId;
        Points = points;
    }
}
