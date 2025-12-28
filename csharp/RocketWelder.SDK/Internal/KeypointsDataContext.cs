using System;
using RocketWelder.SDK.Protocols;

namespace RocketWelder.SDK.Internal;

/// <summary>
/// Unit of Work implementation for keypoints data.
/// Wraps an <see cref="IKeypointsWriter"/> and auto-commits on Commit().
/// </summary>
internal sealed class KeypointsDataContext : IKeypointsDataContext
{
    private readonly IKeypointsWriter _writer;

    public KeypointsDataContext(IKeypointsWriter writer, ulong frameId)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        FrameId = frameId;
    }

    public ulong FrameId { get; }

    public void Add(Keypoint point, int x, int y, float confidence)
    {
        _writer.Append(point.Id, x, y, confidence);
    }

    /// <summary>
    /// Commits the data context by disposing the underlying writer.
    /// Called automatically when the processing delegate returns.
    /// </summary>
    internal void Commit()
    {
        _writer.Dispose();
    }
}
