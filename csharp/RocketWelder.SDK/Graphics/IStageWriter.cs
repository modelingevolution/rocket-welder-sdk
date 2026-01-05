using System;
using BlazorBlaze.Server;

namespace RocketWelder.SDK.Graphics;

/// <summary>
/// Stage writer for vector graphics overlays.
/// User-facing API only - auto-flushes on dispose like other writers.
/// </summary>
/// <remarks>
/// <para>
/// This interface provides access to layer canvases for drawing vector graphics
/// that are streamed directly to the browser via Protocol V2.
/// </para>
/// <para>
/// The writer auto-flushes on dispose, following the same pattern as
/// ISegmentationResultWriter and IKeyPointsWriter.
/// </para>
/// <para>
/// <b>Example:</b>
/// <code>
/// client.Start((input, segWriter, kpWriter, stageWriter, output) =>
/// {
///     // Draw on layer 0 (background)
///     stageWriter[0].SetStroke(RgbColor.Red);
///     stageWriter[0].DrawPolygon(contourPoints);
///
///     // Draw on layer 1 (labels)
///     stageWriter[1].DrawText($"Frame: {stageWriter.FrameId}", 10, 20);
/// });
/// // stageWriter auto-flushes on dispose (via using statement in SDK)
/// </code>
/// </para>
/// </remarks>
public interface IStageWriter : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets the current frame ID.
    /// </summary>
    ulong FrameId { get; }

    /// <summary>
    /// Gets the layer canvas for the specified layer ID.
    /// Layers are composited with lower IDs at the back (0 = bottom).
    /// </summary>
    /// <param name="layerId">Layer ID (0-15)</param>
    /// <returns>The layer canvas for drawing operations</returns>
    ILayerCanvas this[byte layerId] { get; }

    /// <summary>
    /// Gets the layer canvas for the specified layer ID.
    /// Alternative method syntax for the indexer.
    /// </summary>
    /// <param name="layerId">Layer ID (0-15)</param>
    /// <returns>The layer canvas for drawing operations</returns>
    ILayerCanvas Layer(byte layerId);
}
