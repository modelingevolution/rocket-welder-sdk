using System.Net.Http;

namespace RocketWelder.SDK.Http;

/// <summary>
/// Decides "station unreachable" once for every SDK caller (epic-128 feature-001 design.md §4, ADR-13). Registered by
/// <see cref="ServiceCollectionExtensions.AddRocketWelderClient"/>.
/// <list type="bullet">
/// <item>A connection or name-resolution failure becomes <see cref="StationUnreachableException"/> saying nothing was
/// done.</item>
/// <item>No answer within the timeout becomes <see cref="StationUnreachableException"/> saying the request may or may
/// not have been applied. A cancellation by the caller stays an <see cref="OperationCanceledException"/>.</item>
/// </list>
/// </summary>
/// <remarks>
/// A <see cref="DelegatingHandler"/> receives one token that merges the caller's token with
/// <see cref="HttpClient.Timeout"/>, so it cannot tell the two apart. The handler therefore owns the timeout:
/// <see cref="ServiceCollectionExtensions.AddRocketWelderClient"/> hands it the configured
/// <see cref="HttpClient.Timeout"/> and sets the client's own to infinite. The handler buffers the response body inside
/// that timeout, so the whole exchange is bounded as before.
/// </remarks>
internal sealed class StationUnreachableHandler(StationClientSettings settings) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var current = settings.Current; // one read: base address and timeout from the same configuration
        var station = current.BaseAddress ?? Authority(request.RequestUri);
        var timeout = current.Timeout is { } t && t != Timeout.InfiniteTimeSpan ? t : (TimeSpan?)null;

        using var timer = timeout is { } limit ? new CancellationTokenSource(limit) : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timer.Token);
        HttpResponseMessage? response = null;
        try
        {
            response = await base.SendAsync(request, linked.Token).ConfigureAwait(false);
            if (timeout is not null)
                await response.Content.LoadIntoBufferAsync(linked.Token).ConfigureAwait(false);
            return response;
        }
        catch (HttpRequestException ex) when (ex is not StationUnreachableException && ex.HttpRequestError is HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError)
        {
            response?.Dispose();
            throw StationUnreachableException.NothingDone(station, ex);
        }
        catch (OperationCanceledException ex) when (timer.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            response?.Dispose();
            throw StationUnreachableException.NoAnswer(station, timeout!.Value, ex);
        }
        catch
        {
            response?.Dispose();
            throw;
        }
    }

    private static Uri Authority(Uri? requestUri) =>
        requestUri is { IsAbsoluteUri: true } uri ? new Uri(uri.GetLeftPart(UriPartial.Authority) + "/") : new Uri("unknown:/");
}

/// <summary>One configuration of the client: the base address the handler names and the timeout it enforces.</summary>
internal sealed record StationEndpoint(Uri? BaseAddress, TimeSpan? Timeout);

/// <summary>
/// What <see cref="StationUnreachableHandler"/> names and enforces. Each configured client publishes one immutable
/// <see cref="StationEndpoint"/>, swapped by reference, so a handler never reads a base address from one configuration
/// and a timeout from another.
/// </summary>
internal sealed class StationClientSettings
{
    private StationEndpoint _current = new(null, null);

    public StationEndpoint Current => Volatile.Read(ref _current);

    /// <summary>Takes over a configured client's base address and timeout; the client's own timeout becomes infinite so the
    /// handler alone decides a timeout (see <see cref="StationUnreachableHandler"/> remarks).</summary>
    public void TakeOver(HttpClient http)
    {
        Volatile.Write(ref _current, new StationEndpoint(http.BaseAddress, http.Timeout));
        http.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
    }
}
