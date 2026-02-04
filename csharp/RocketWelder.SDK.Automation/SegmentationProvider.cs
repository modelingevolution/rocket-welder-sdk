using System.Buffers;
using System.Drawing;
using RocketWelder.SDK.Protocols;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// Wraps an <see cref="ISegmentationResultSource"/> (push/streaming) into an
/// <see cref="ISegmentationProvider"/> (pull/query-latest).
/// Runs a background loop consuming the source stream and caching the latest frame.
/// </summary>
public sealed class SegmentationProvider : ISegmentationProvider, IAsyncDisposable
{
    private readonly ISegmentationResultSource _source;
    private readonly object _lock = new();
    private SegmentationFrame? _latest;
    private CancellationTokenSource? _cts;
    private Task? _backgroundTask;

    public SegmentationProvider(ISegmentationResultSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public bool HasData
    {
        get { lock (_lock) return _latest != null; }
    }

    public SegmentationFrameHandle GetLatest()
    {
        SegmentationFrame frame;
        lock (_lock)
        {
            if (_latest == null)
                throw new InvalidOperationException("No segmentation data available yet. Check HasData before calling GetLatest().");
            frame = _latest.Value;
        }

        var instances = frame.Instances;

        // Count total points across all instances
        int totalPoints = 0;
        for (int i = 0; i < instances.Count; i++)
            totalPoints += instances[i].Points.Length;

        // Rent pooled buffers
        var pointsBuffer = ArrayPool<Point>.Shared.Rent(Math.Max(totalPoints, 1));
        var instancesBuffer = ArrayPool<SegmentationInstance>.Shared.Rent(Math.Max(instances.Count, 1));

        // Copy points contiguously and rebuild instances with sliced references
        int pointOffset = 0;
        for (int i = 0; i < instances.Count; i++)
        {
            var src = instances[i];
            var pts = src.Points;
            pts.Span.CopyTo(pointsBuffer.AsSpan(pointOffset, pts.Length));

            // Create instance pointing into the pooled points buffer
            instancesBuffer[i] = new SegmentationInstance(
                src.ClassId,
                src.InstanceId,
                src.Confidence,
                pointsBuffer.AsMemory(pointOffset, pts.Length));

            pointOffset += pts.Length;
        }

        return new SegmentationFrameHandle(
            frame.FrameId,
            frame.Width,
            frame.Height,
            pointsBuffer, totalPoints,
            instancesBuffer, instances.Count);
    }

    /// <summary>
    /// Starts the background consumption loop.
    /// </summary>
    public void Start(CancellationToken ct = default)
    {
        if (_backgroundTask != null)
            return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _backgroundTask = Task.Run(() => ConsumeLoopAsync(_cts.Token), _cts.Token);
    }

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var frame in _source.ReadFramesAsync(ct).ConfigureAwait(false))
            {
                lock (_lock) _latest = frame;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts != null)
        {
            await _cts.CancelAsync();
            if (_backgroundTask != null)
            {
                try { await _backgroundTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            _cts.Dispose();
        }
    }
}
