using RocketWelder.SDK.Automation.AdaptivePoints;
using RocketWelder.SDK.Automation.Vision;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// Camera device abstraction providing access to live image frames.
/// Frames are returned as <see cref="MatFrame"/> with zero-copy RAII semantics:
/// decoded pixels stay in pooled memory, pinned and wrapped in a Mat.
/// Caller must dispose the <see cref="MatFrame"/> to return the buffer to the pool.
/// </summary>
public interface ICamera : IDevice
{
    /// <summary>
    /// The serial number identifying this camera (matches GstPylonSrc.DeviceSerialNumber).
    /// </summary>
    string SerialNumber { get; }

    /// <summary>
    /// True when a pipeline with this camera is running and the MJPEG stream is available.
    /// </summary>
    bool IsStreaming { get; }

    /// <summary>
    /// Returns the latest decoded frame as a pinned Mat, or null if no frame is available.
    /// Caller must dispose the returned <see cref="MatFrame"/> to return pooled memory.
    /// </summary>
    MatFrame? TryGetImage();

    /// <summary>
    /// Camera projector for pixel-to-3D projection using this camera's intrinsics
    /// and hand-eye calibration.
    /// </summary>
    ICameraProjector Projector { get; }

    /// <summary>
    /// Camera-bound view locator. Computes TCP poses that put this camera in a usable
    /// view of a target. Standoff and roll convention are baked into the implementation
    /// supplied by the host at camera construction.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown by the getter (or by methods on the returned locator) when no
    /// <see cref="IRobot"/> is registered, since locator output is a TCP pose.
    /// </exception>
    ICameraViewLocator Locator { get; }
}
