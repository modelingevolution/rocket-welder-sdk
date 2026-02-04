namespace RocketWelder.SDK.Automation;

/// <summary>
/// Provides "get latest" access to segmentation results.
/// Each call to <see cref="IDataProvider{T}.GetLatest"/> returns a new <see cref="SegmentationFrameHandle"/>
/// that the caller must dispose to return pooled buffers.
/// </summary>
public interface ISegmentationProvider : IDataProvider<SegmentationFrameHandle>
{
}
