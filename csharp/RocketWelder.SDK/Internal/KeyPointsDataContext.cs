using System;
using RocketWelder.SDK.Protocols;

namespace RocketWelder.SDK.Internal;

/// <summary>
/// Unit of Work implementation for keypoints data.
/// Wraps an <see cref="IKeyPointsWriter"/> and commits on Dispose.
/// </summary>
internal sealed class KeyPointsDataContext : IKeyPointsDataContext
{
    private readonly IKeyPointsWriter _writer;
    private bool _disposed;

    public KeyPointsDataContext(IKeyPointsWriter writer, ulong frameId)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        FrameId = frameId;
    }

    public ulong FrameId { get; }

    public void Add(KeyPoint point, int x, int y, float confidence)
    {
        _writer.Append(point.Id, x, y, confidence);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _writer.Dispose();
    }
}
