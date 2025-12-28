using System;
using System.Threading.Tasks;

namespace RocketWelder.SDK.Transport
{
    /// <summary>
    /// Low-level abstraction for writing discrete frames to any transport.
    /// Transport-agnostic interface that handles the question: "where do frames go?"
    /// </summary>
    /// <remarks>
    /// This abstraction decouples protocol logic (Keypoints, SegmentationResults) from
    /// transport mechanisms (File, NNG, TCP, WebSocket). Each frame is written atomically.
    /// </remarks>
    public interface IFrameSink : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// Writes a complete frame to the underlying transport synchronously.
        /// </summary>
        /// <param name="frameData">Complete frame data to write</param>
        void WriteFrame(ReadOnlySpan<byte> frameData);

        /// <summary>
        /// Writes a complete frame to the underlying transport asynchronously.
        /// </summary>
        /// <param name="frameData">Complete frame data to write</param>
        ValueTask WriteFrameAsync(ReadOnlyMemory<byte> frameData);

        /// <summary>
        /// Flushes any buffered data to the transport synchronously.
        /// For message-based transports (NNG), this may be a no-op.
        /// </summary>
        void Flush();

        /// <summary>
        /// Flushes any buffered data to the transport asynchronously.
        /// For message-based transports (NNG), this may be a no-op.
        /// </summary>
        Task FlushAsync();
    }
}
