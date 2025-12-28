using System.Drawing;

namespace RocketWelder.SDK.Protocols;

/// <summary>
/// Represents a single keypoint in a pose estimation result.
/// Used for both encoding and decoding keypoints data.
/// </summary>
/// <param name="Id">Keypoint identifier (e.g., 0=nose, 1=left_eye, etc.)</param>
/// <param name="Position">Position of the keypoint in pixel coordinates.</param>
/// <param name="Confidence">Confidence score (uses full ushort precision 0-65535).</param>
public readonly record struct Keypoint(int Id, Point Position, Confidence Confidence)
{
    /// <summary>
    /// Creates a keypoint with explicit x, y coordinates and raw ushort confidence.
    /// </summary>
    public Keypoint(int id, int x, int y, ushort confidence)
        : this(id, new Point(x, y), (Confidence)confidence) { }

    /// <summary>
    /// Creates a keypoint with explicit x, y coordinates and float confidence (0.0-1.0).
    /// </summary>
    public Keypoint(int id, int x, int y, float confidence)
        : this(id, new Point(x, y), confidence) { }

    /// <summary>
    /// Creates a keypoint with position and float confidence (0.0-1.0).
    /// </summary>
    public Keypoint(int id, Point position, float confidence)
        : this(id, position, (Confidence)confidence) { }
}
