using System.Buffers;
using RocketWelder.SDK.Protocols;

namespace RocketWelder.SDK.Runtime;

/// <summary>
/// Wraps an <see cref="IKeyPointsSource"/> (push/streaming) into an
/// <see cref="IKeyPointsProvider"/> (pull/query-latest).
/// Runs a background loop consuming the source stream and caching the latest frame.
/// Delta resolution is already handled by the source.
/// </summary>
public sealed class KeyPointsProvider : IKeyPointsProvider, IAsyncDisposable
{
    private readonly IKeyPointsSource _source;
    private readonly object _lock = new();
    private DeltaFrame<KeyPoint>? _latest;
    private CancellationTokenSource? _cts;
    private Task? _backgroundTask;

    public KeyPointsProvider(IKeyPointsSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public bool HasData(byte cameraNo = 0)
    {
        // Single-camera provider — only camera 0 is supported
        if (cameraNo != 0) return false;
        lock (_lock) return _latest != null;
    }

    public KeyPointsFrameHandle GetLatest(byte cameraNo = 0)
    {
        if (cameraNo != 0)
            throw new InvalidOperationException($"KeyPointsProvider only supports camera 0, got {cameraNo}.");

        DeltaFrame<KeyPoint> frame;
        lock (_lock)
        {
            if (_latest == null)
                throw new InvalidOperationException("No keypoints data available yet. Check HasData() before calling GetLatest().");
            frame = _latest.Value;
        }

        var items = frame.Items.Span;
        var buffer = ArrayPool<KeyPoint>.Shared.Rent(items.Length);
        items.CopyTo(buffer);

        return new KeyPointsFrameHandle(frame.FrameId, buffer, items.Length);
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
