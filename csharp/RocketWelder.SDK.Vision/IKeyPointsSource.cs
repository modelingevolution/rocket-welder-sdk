using System;
using System.Collections.Generic;
using System.Threading;
using RocketWelder.SDK.Protocols;

// Import DeltaFrame<KeyPoint> for streaming use - uses Protocols.KeyPoint with ushort confidence
// Use .NormalizedConfidence() extension to get float 0.0-1.0 value
using DeltaKeyPointsFrame = RocketWelder.SDK.Protocols.DeltaFrame<RocketWelder.SDK.Protocols.KeyPoint>;

namespace RocketWelder.SDK.Vision;

/// <summary>
/// Streaming reader for keypoints via IAsyncEnumerable.
/// Designed for real-time streaming over TCP/WebSocket/NNG.
/// Returns DeltaFrame&lt;KeyPoint&gt; which includes IsDelta for streaming context.
/// </summary>
public interface IKeyPointsSource : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Stream frames as they arrive from the transport.
    /// Supports cancellation and backpressure.
    /// Returns DeltaFrame with IsDelta indicating master vs delta frame.
    /// </summary>
    IAsyncEnumerable<DeltaKeyPointsFrame> ReadFramesAsync(CancellationToken cancellationToken = default);
}
