using System;

namespace RocketWelder.SDK.Runtime;

/// <summary>
/// Provides "get latest" access to streaming data with copy-on-read semantics.
/// Each call to <see cref="GetLatest"/> returns a new handle backed by ArrayPool
/// that the caller owns and must dispose.
/// Data is keyed by camera number to support multi-camera setups.
/// </summary>
/// <typeparam name="T">The handle type, which must be disposable to return pooled buffers.</typeparam>
public interface IDataProvider<T> where T : IDisposable
{
    /// <summary>
    /// Gets a copy of the latest data frame for the given camera.
    /// The caller owns the returned handle and must dispose it.
    /// </summary>
    /// <param name="cameraNo">Camera index (default 0 for single-camera setups).</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no data is available yet (<see cref="HasData"/> is false).
    /// </exception>
    T GetLatest(byte cameraNo = 0);

    /// <summary>
    /// True when at least one frame has been received for the given camera.
    /// </summary>
    /// <param name="cameraNo">Camera index (default 0 for single-camera setups).</param>
    bool HasData(byte cameraNo = 0);
}
