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
public sealed class StationUnreachableHandler : DelegatingHandler
{
    private readonly StationClientSettings _settings;

    /// <summary>A handler for a hand-built <see cref="HttpClient"/>.</summary>
    /// <param name="baseAddress">The station's base address, named in the message; <see langword="null"/> names the
    /// request's scheme, host and port.</param>
    /// <param name="timeout">The time the station has to answer; <see langword="null"/> leaves timeouts to
    /// <see cref="HttpClient.Timeout"/> (then only connection failures are mapped).</param>
    public StationUnreachableHandler(Uri? baseAddress = null, TimeSpan? timeout = null)
        : this(new StationClientSettings { BaseAddress = baseAddress, Timeout = timeout })
    {
    }

    internal StationUnreachableHandler(StationClientSettings settings) => _settings = settings;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var station = _settings.BaseAddress ?? Authority(request.RequestUri);
        var timeout = _settings.Timeout is { } t && t != Timeout.InfiniteTimeSpan ? t : (TimeSpan?)null;

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

/// <summary>What <see cref="StationUnreachableHandler"/> names and enforces: the client's base address and timeout.</summary>
internal sealed class StationClientSettings
{
    public Uri? BaseAddress { get; set; }

    public TimeSpan? Timeout { get; set; }

    /// <summary>Takes over a configured client's base address and timeout; the client's own timeout becomes infinite so the
    /// handler alone decides a timeout (see <see cref="StationUnreachableHandler"/> remarks).</summary>
    public void TakeOver(HttpClient http)
    {
        BaseAddress = http.BaseAddress;
        Timeout = http.Timeout;
        http.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
    }
}
