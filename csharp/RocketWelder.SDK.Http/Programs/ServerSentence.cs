using System.Text.Json;

namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Reads rw2's error body. Value endpoints answer with <c>Results.BadRequest(string)</c> / <c>Results.NotFound(string)</c>:
/// the sentence as a JSON string (design.md §12.1).
/// </summary>
internal static class ServerSentence
{
    /// <summary>The trimmed raw body, and the sentence when the body is a JSON string (otherwise <see langword="null"/>).</summary>
    public static async Task<(string Raw, string? Sentence)> ReadAsync(HttpResponseMessage res, CancellationToken ct)
    {
        var raw = (await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim();
        if (!raw.StartsWith('"'))
            return (raw, null);
        try
        {
            return (raw, JsonSerializer.Deserialize<string>(raw));
        }
        catch (JsonException)
        {
            return (raw, null);   // not a well-formed JSON string
        }
    }

    /// <summary>
    /// The message rule of design.md §12.6: the JSON-string sentence, else the raw body; <see langword="null"/> when the
    /// body is empty or whitespace (the caller then uses a fixed text naming the status).
    /// </summary>
    public static async Task<string?> MessageAsync(HttpResponseMessage res, CancellationToken ct)
    {
        var (raw, sentence) = await ReadAsync(res, ct).ConfigureAwait(false);
        var message = sentence ?? raw;
        return string.IsNullOrWhiteSpace(message) ? null : message;
    }

    /// <summary>
    /// A failed response as an <see cref="HttpRequestException"/> carrying the status:
    /// <c>"{action} failed: HTTP 404 (NotFound). {message}"</c>, the message per <see cref="MessageAsync"/>.
    /// </summary>
    public static async Task<HttpRequestException> FailureAsync(HttpResponseMessage res, string action, CancellationToken ct)
    {
        var message = await MessageAsync(res, ct).ConfigureAwait(false);
        return new HttpRequestException(
            $"{action} failed: HTTP {(int)res.StatusCode} ({res.StatusCode}).{(message is null ? string.Empty : $" {message}")}",
            inner: null,
            statusCode: res.StatusCode);
    }
}
