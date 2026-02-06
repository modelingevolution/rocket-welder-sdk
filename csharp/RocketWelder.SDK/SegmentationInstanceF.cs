using System;
using ModelingEvolution.Drawing;
using RocketWelder.SDK.Protocols;

namespace RocketWelder.SDK;

/// <summary>
/// Float-precision segmentation instance with <see cref="Point{T}"/> coordinates
/// suitable for direct <see cref="Polygon{T}"/> construction.
/// Points is a <see cref="ReadOnlyMemory{T}"/> slice — may share a pooled buffer
/// with other instances in the same frame.
/// </summary>
/// <param name="ClassId">Class identifier (e.g., 0=person, 1=car, etc.)</param>
/// <param name="InstanceId">Instance identifier within the class.</param>
/// <param name="Confidence">Detection confidence score (0.0-1.0).</param>
/// <param name="Points">Polygon points in pixel coordinates as Point&lt;float&gt;.</param>
public readonly record struct SegmentationInstanceF(
    byte ClassId,
    byte InstanceId,
    Confidence Confidence,
    ReadOnlyMemory<Point<float>> Points)
{
    /// <summary>
    /// Creates a <see cref="Polygon{T}"/> from the points. Zero-copy when backed by an array.
    /// The polygon is valid only while the owning handle is not disposed.
    /// </summary>
    public Polygon<float> ToPolygon() => new(Points);
}
