using System;
using System.Drawing;

namespace RocketWelder.SDK.Protocols;

/// <summary>
/// Represents a single segmentation instance (object mask) in a frame.
/// Contains the class, instance ID, confidence score, and polygon points defining the mask boundary.
/// </summary>
/// <param name="ClassId">Class identifier (e.g., 0=person, 1=car, etc.)</param>
/// <param name="InstanceId">Instance identifier within the class (for distinguishing multiple objects of same class).</param>
/// <param name="Confidence">Detection confidence score (0.0-1.0).</param>
/// <param name="Points">Polygon points defining the segmentation mask boundary in pixel coordinates.</param>
public readonly record struct SegmentationInstance(byte ClassId, byte InstanceId, Confidence Confidence, ReadOnlyMemory<Point> Points)
{
    /// <summary>
    /// Creates an instance with a Point array.
    /// </summary>
    public SegmentationInstance(byte classId, byte instanceId, Confidence confidence, Point[] points)
        : this(classId, instanceId, confidence, (ReadOnlyMemory<Point>)points) { }
}

/// <summary>
/// Extension methods for <see cref="SegmentationInstance"/>.
/// </summary>
public static class SegmentationInstanceExtensions
{
    /// <summary>
    /// Converts points to normalized coordinates [0-1] range.
    /// </summary>
    /// <param name="instance">The segmentation instance.</param>
    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    /// <returns>Array of normalized points.</returns>
    public static PointF[] ToNormalized(this SegmentationInstance instance, uint width, uint height)
    {
        if (width == 0 || height == 0)
            throw new ArgumentException("Width and height must be greater than zero");

        var points = instance.Points.Span;
        var result = new PointF[points.Length];
        float widthF = width;
        float heightF = height;

        for (int i = 0; i < points.Length; i++)
        {
            result[i] = new PointF(points[i].X / widthF, points[i].Y / heightF);
        }

        return result;
    }
}
