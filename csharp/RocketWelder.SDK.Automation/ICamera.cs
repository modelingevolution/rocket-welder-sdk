namespace RocketWelder.SDK.Automation;

/// <summary>
/// Camera device abstraction providing access to live image frames.
/// Frames are returned as <see cref="MatFrame"/> with zero-copy RAII semantics:
/// decoded pixels stay in pooled memory, pinned and wrapped in a Mat.
/// Caller must dispose the <see cref="MatFrame"/> to return the buffer to the pool.
/// </summary>
public interface ICamera : IDisposable
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
}
