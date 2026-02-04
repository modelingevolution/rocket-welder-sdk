namespace RocketWelder.SDK.Automation;

/// <summary>
/// Provides "get latest" access to keypoints detection results.
/// Each call to <see cref="IDataProvider{T}.GetLatest"/> returns a new <see cref="KeyPointsFrameHandle"/>
/// that the caller must dispose to return pooled buffers.
/// </summary>
public interface IKeyPointsProvider : IDataProvider<KeyPointsFrameHandle>
{
}
