using System.Net;

namespace RocketWelder.SDK.Http.Tests.Programs;

/// <summary>
/// Captures the single request an SDK method issues and returns a canned response.
/// Records the verb, URI, <c>If-Match</c> header and body so a test can assert the
/// exact wire shape without a live server.
/// </summary>
internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public RecordingHandler(HttpStatusCode status, string? json = null)
        : this(_ => Respond(status, json))
    {
    }

    public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    public HttpMethod? Method { get; private set; }
    public Uri? RequestUri { get; private set; }
    public string? IfMatch { get; private set; }
    public bool IfMatchPresent { get; private set; }
    public string? Body { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Method = request.Method;
        RequestUri = request.RequestUri;
        IfMatchPresent = request.Headers.TryGetValues("If-Match", out var values);
        IfMatch = IfMatchPresent ? string.Join(",", values!) : null;
        if (request.Content is not null)
            Body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return _responder(request);
    }

    public static HttpClient Client(RecordingHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://welder.test/") };

    private static HttpResponseMessage Respond(HttpStatusCode status, string? json)
    {
        var res = new HttpResponseMessage(status);
        if (json is not null)
            res.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        return res;
    }
}
