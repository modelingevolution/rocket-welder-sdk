using System.Drawing;

namespace RocketWelder.BinaryProtocol;

/// <summary>
/// Represents a single keypoint in a pose estimation result.
/// Used for both encoding and decoding keypoints data.
/// </summary>
public readonly struct Keypoint
{
    /// <summary>
    /// Keypoint identifier (e.g., 0=nose, 1=left_eye, etc.)
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Position of the keypoint in pixel coordinates.
    /// </summary>
    public Point Position { get; init; }

    /// <summary>
    /// Confidence score (0-10000 representing 0.0-1.0)
    /// </summary>
    public ushort Confidence { get; init; }

    public Keypoint(int id, Point position, ushort confidence)
    {
        Id = id;
        Position = position;
        Confidence = confidence;
    }

    public Keypoint(int id, int x, int y, ushort confidence)
    {
        Id = id;
        Position = new Point(x, y);
        Confidence = confidence;
    }
}
