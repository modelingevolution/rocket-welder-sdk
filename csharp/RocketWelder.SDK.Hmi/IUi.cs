using System;

namespace RocketWelder.SDK.Hmi;

/// <summary>
/// Per-frame UI writer with multi-layer support.
/// Wraps <see cref="IStageWriter"/> and exposes <see cref="IUiLayer"/> per layer.
/// Auto-flushes on dispose.
/// </summary>
public interface IUi : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets the current frame ID (sourced from the segmentation stream).
    /// </summary>
    ulong FrameId { get; }

    /// <summary>
    /// Gets the UI layer for the specified layer ID.
    /// Layers are composited with lower IDs at the back (0 = bottom).
    /// </summary>
    /// <param name="layerId">Layer ID (0-15)</param>
    IUiLayer this[byte layerId] { get; }

    /// <summary>
    /// Gets the UI layer for the specified layer ID.
    /// Alternative method syntax for the indexer.
    /// </summary>
    /// <param name="layerId">Layer ID (0-15)</param>
    IUiLayer Layer(byte layerId);
}
