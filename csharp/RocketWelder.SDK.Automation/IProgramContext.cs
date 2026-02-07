using Microsoft.Extensions.Logging;
using RocketWelder.SDK.Graphics;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// Context provided to programs during execution.
/// Provides access to ML data providers and registered devices.
/// </summary>
public interface IProgramContext
{
    /// <summary>
    /// Access to keypoint detection results from ML pipeline.
    /// Use <see cref="IDataProvider{T}.HasData(byte)"/> to check availability
    /// before calling <see cref="IDataProvider{T}.GetLatest"/>.
    /// </summary>
    IKeyPointsProvider Keypoints { get; }

    /// <summary>
    /// Access to segmentation results from ML pipeline.
    /// Use <see cref="IDataProvider{T}.HasData(byte)"/> to check availability
    /// before calling <see cref="IDataProvider{T}.GetLatest"/>.
    /// </summary>
    ISegmentationProvider Segmentation { get; }

    /// <summary>
    /// Logger for program output.
    /// </summary>
    ILogger Logger { get; }

    /// <summary>
    /// Access to the UI rendering sink for drawing overlays.
    /// Creates per-frame writers that auto-flush on dispose.
    /// The frame ID is automatically sourced from the segmentation stream.
    /// </summary>
    IUiSink Ui { get; }

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

    /// <summary>
    /// Gets a registered device by type and device number.
    /// Returns null if device not found.
    /// </summary>
    /// <typeparam name="T">The device interface type (e.g., ICobot, IPlc).</typeparam>
    /// <param name="id">The device number (auto-assigned during creation).</param>
    T? GetById<T>(uint id) where T : class;
}
