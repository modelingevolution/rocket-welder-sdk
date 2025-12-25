using System;

namespace RocketWelder.SDK.Graphics;

/// <summary>
/// Factory for creating per-frame stage writers (transport-agnostic).
/// Follows the same pattern as ISegmentationResultSink and IKeyPointsSink.
/// </summary>
public interface IStageSink : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Creates a writer for the specified frame.
    /// The writer auto-flushes on dispose.
    /// </summary>
    /// <param name="frameId">Frame identifier</param>
    /// <returns>Stage writer that auto-flushes on dispose</returns>
    IStageWriter CreateWriter(ulong frameId);
}
