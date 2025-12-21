using System;
using System.Threading.Tasks;

namespace RocketWelder.SDK.Transport;

/// <summary>
/// A frame sink that discards all data.
/// Use when no output URL is configured or for testing.
/// </summary>
public sealed class NullFrameSink : IFrameSink
{
    /// <summary>
    /// Singleton instance of NullFrameSink.
    /// </summary>
    public static readonly NullFrameSink Instance = new();

    private NullFrameSink() { }

    /// <summary>
    /// Discards the frame data (no-op).
    /// </summary>
    public void WriteFrame(ReadOnlySpan<byte> frameData)
    {
        // Intentionally empty - discard data
    }

    /// <summary>
    /// Discards the frame data (no-op).
    /// </summary>
    public ValueTask WriteFrameAsync(ReadOnlyMemory<byte> frameData)
    {
        // Intentionally empty - discard data
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// No-op flush.
    /// </summary>
    public void Flush()
    {
        // Nothing to flush
    }

    /// <summary>
    /// No-op flush.
    /// </summary>
    public Task FlushAsync() => Task.CompletedTask;

    /// <summary>
    /// No-op dispose (singleton, never actually disposed).
    /// </summary>
    public void Dispose()
    {
        // Singleton - never dispose
    }

    /// <summary>
    /// No-op dispose (singleton, never actually disposed).
    /// </summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
