using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RocketWelder.SDK.Transport;

namespace RocketWelder.SDK;

/// <summary>
/// A graphics frame containing vector graphics overlay data.
/// </summary>
/// <param name="FrameId">Frame identifier matching the video frame.</param>
/// <param name="Data">Raw graphics data (Protocol V2 vector graphics format).</param>
public readonly record struct GraphicsFrame(ulong FrameId, ReadOnlyMemory<byte> Data);

/// <summary>
/// Streaming reader for graphics frames via IAsyncEnumerable.
/// Designed for real-time streaming of vector graphics overlays.
/// </summary>
public interface IGraphicsSource : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Stream frames as they arrive from the transport.
    /// </summary>
    IAsyncEnumerable<GraphicsFrame> ReadFramesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Streaming reader for graphics frames.
/// Reads frames from IFrameSource and yields them via IAsyncEnumerable.
/// </summary>
public class GraphicsSource : IGraphicsSource
{
    private readonly IFrameSource _frameSource;
    private bool _disposed;

    public GraphicsSource(IFrameSource frameSource)
    {
        _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
    }

    public async IAsyncEnumerable<GraphicsFrame> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested && !_disposed)
        {
            var frameData = await _frameSource.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
            if (frameData.IsEmpty)
                yield break;

            // Extract frame_id from header (first 8 bytes, little-endian)
            if (frameData.Length < 8)
                continue;

            var frameId = BitConverter.ToUInt64(frameData.Span[..8]);
            yield return new GraphicsFrame(frameId, frameData);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _frameSource.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _frameSource.DisposeAsync().ConfigureAwait(false);
    }
}
