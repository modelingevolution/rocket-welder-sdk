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
}
