using Microsoft.Extensions.Logging;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// Context provided to programs during execution.
/// Provides access to ML data sources and registered devices.
/// </summary>
public interface IProgramContext
{
    /// <summary>
    /// Access to keypoint detection results from ML pipeline.
    /// </summary>
    IKeyPointsSource Keypoints { get; }

    /// <summary>
    /// Access to segmentation results from ML pipeline.
    /// </summary>
    ISegmentationSource Segmentation { get; }

    /// <summary>
    /// Access to graphics overlays from ML pipeline.
    /// </summary>
    IGraphicsSource Graphics { get; }

    /// <summary>
    /// Logger for program output.
    /// </summary>
    ILogger Logger { get; }

    /// <summary>
    /// True when running in test/dry-run mode.
    /// Programs should log intended actions instead of executing them.
    /// </summary>
    bool IsDryRun { get; }

    /// <summary>
    /// Gets a registered device by type and optional name.
    /// Returns null if device not found.
    /// </summary>
    /// <typeparam name="T">The device interface type (e.g., ICobot, IPlc).</typeparam>
    /// <param name="name">Optional name to distinguish multiple devices of the same type.</param>
    T? GetDevice<T>(string? name = null) where T : class;

    /// <summary>
    /// Gets a registered device by type and optional name.
    /// Throws if device not found.
    /// </summary>
    /// <typeparam name="T">The device interface type (e.g., ICobot, IPlc).</typeparam>
    /// <param name="name">Optional name to distinguish multiple devices of the same type.</param>
    /// <exception cref="InvalidOperationException">Thrown when device is not registered.</exception>
    T GetRequiredDevice<T>(string? name = null) where T : class;
}
