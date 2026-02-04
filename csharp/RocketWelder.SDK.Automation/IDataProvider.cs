using System;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// Provides "get latest" access to streaming data with copy-on-read semantics.
/// Each call to <see cref="GetLatest"/> returns a new handle backed by ArrayPool
/// that the caller owns and must dispose.
/// </summary>
/// <typeparam name="T">The handle type, which must be disposable to return pooled buffers.</typeparam>
public interface IDataProvider<T> where T : IDisposable
{
    /// <summary>
    /// Gets a copy of the latest data frame.
    /// The caller owns the returned handle and must dispose it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no data is available yet (<see cref="HasData"/> is false).
    /// </exception>
    T GetLatest();

    /// <summary>
    /// True when at least one frame has been received and <see cref="GetLatest"/> can be called.
    /// </summary>
    bool HasData { get; }
}
