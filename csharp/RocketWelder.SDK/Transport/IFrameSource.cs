using System;
using System.Threading;
using System.Threading.Tasks;

namespace RocketWelder.SDK.Transport
{
    /// <summary>
    /// Low-level abstraction for reading discrete frames from any transport.
    /// Transport-agnostic interface that handles the question: "where do frames come from?"
    /// </summary>
    /// <remarks>
    /// This abstraction decouples protocol logic (KeyPoints, SegmentationResults) from
    /// transport mechanisms (File, NNG, TCP, WebSocket). Each frame is read atomically.
    /// </remarks>
    public interface IFrameSource : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// Reads a complete frame from the underlying transport synchronously.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Complete frame data, or empty if end of stream/no more messages</returns>
        ReadOnlyMemory<byte> ReadFrame(CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads a complete frame from the underlying transport asynchronously.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Complete frame data, or empty if end of stream/no more messages</returns>
        ValueTask<ReadOnlyMemory<byte>> ReadFrameAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if more frames are available.
        /// For streaming transports (file), this checks for EOF.
        /// For message-based transports (NNG), this may always return true until disconnection.
        /// </summary>
        bool HasMoreFrames { get; }
    }
}
