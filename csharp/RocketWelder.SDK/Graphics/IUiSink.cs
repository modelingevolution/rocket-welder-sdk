using System;

namespace RocketWelder.SDK.Graphics;

/// <summary>
/// Factory for creating per-frame <see cref="IUi"/> writers.
/// The frame ID is sourced automatically from a <see cref="Func{TResult}"/> connected
/// to the segmentation provider's current frame.
/// </summary>
public interface IUiSink : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Creates a UI writer for the current frame.
    /// The frame ID is obtained from the connected segmentation stream.
    /// The writer auto-flushes on dispose.
    /// </summary>
    IUi CreateWriter();

    /// <summary>
    /// Viewport width in pixels.
    /// </summary>
    uint Width { get; }

    /// <summary>
    /// Viewport height in pixels.
    /// </summary>
    uint Height { get; }
}
