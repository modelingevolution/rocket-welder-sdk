using System;
using System.Threading;
using System.Threading.Tasks;
using Emgu.CV;

namespace RocketWelder.SDK;

/// <summary>
/// Main entry point for RocketWelder SDK high-level API.
/// Provides schema definitions and frame processing loop.
/// </summary>
public interface IRocketWelderClient : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Schema for defining keypoints.
    /// </summary>
    IKeyPointsSchema KeyPoints { get; }

    /// <summary>
    /// Schema for defining segmentation classes.
    /// </summary>
    ISegmentationSchema Segmentation { get; }

    /// <summary>
    /// Starts the processing loop with full context (keypoints + segmentation).
    /// </summary>
    /// <param name="processFrame">
    /// Delegate called for each frame with:
    /// - inputFrame: Source video frame (Mat)
    /// - segmentation: Segmentation data context (UoW)
    /// - keypoints: KeyPoints data context (UoW)
    /// - outputFrame: Output frame for visualization (Mat)
    /// </param>
    /// <param name="cancellationToken">Cancellation token to stop processing</param>
    Task StartAsync(
        Action<Mat, ISegmentationDataContext, IKeyPointsDataContext, Mat> processFrame,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the processing loop (keypoints only).
    /// </summary>
    Task StartAsync(
        Action<Mat, IKeyPointsDataContext, Mat> processFrame,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the processing loop (segmentation only).
    /// </summary>
    Task StartAsync(
        Action<Mat, ISegmentationDataContext, Mat> processFrame,
        CancellationToken cancellationToken = default);
}
