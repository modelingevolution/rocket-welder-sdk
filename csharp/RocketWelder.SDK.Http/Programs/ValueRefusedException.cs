namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Thrown when rw2 refuses a value write (HTTP 400) — a band that does not parse, an unknown device property. The
/// <see cref="Reason"/> is rw2's one-sentence refusal, fit to show to the caller (design.md §12.1, §12.6).
/// </summary>
public sealed class ValueRefusedException(string reason) : Exception(reason)
{
    /// <summary>rw2's refusal sentence, e.g. "accept must not exceed review — write it as accept:review, e.g. 1.2:3.5."</summary>
    public string Reason { get; } = reason;

    // design.md §12.6 message rule: the JSON-string sentence, else the raw body (plain text from an older endpoint),
    // else a fixed text naming the status.
    internal static async Task<ValueRefusedException> FromAsync(HttpResponseMessage res, CancellationToken ct)
    {
        var (raw, sentence) = await ServerSentence.ReadAsync(res, ct).ConfigureAwait(false);
        var reason = sentence ?? raw;
        return new ValueRefusedException(string.IsNullOrWhiteSpace(reason)
            ? $"HTTP {(int)res.StatusCode} ({res.StatusCode}) with no reason."
            : reason);
    }
}
