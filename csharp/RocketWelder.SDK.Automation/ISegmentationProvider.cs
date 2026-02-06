namespace RocketWelder.SDK.Automation;

/// <summary>
/// Provides "get latest" access to segmentation results with float-precision points.
/// Each call to <see cref="IDataProvider{T}.GetLatest"/> returns a new <see cref="SegmentationFrameHandleF"/>
/// that the caller must dispose to return pooled buffers.
/// Instances contain <see cref="ModelingEvolution.Drawing.Point{T}"/> (float) for
/// zero-copy <see cref="ModelingEvolution.Drawing.Polygon{T}"/> construction.
/// </summary>
public interface ISegmentationProvider : IDataProvider<SegmentationFrameHandleF>
{
}
