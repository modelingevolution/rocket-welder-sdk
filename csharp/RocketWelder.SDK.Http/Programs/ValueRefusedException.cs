namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Thrown when rw2 refuses a value write (HTTP 400) — a band that does not parse, an unknown device property. The
/// <see cref="Reason"/> is rw2's one-sentence refusal, fit to show to the caller (design.md §12.1, §12.6).
/// </summary>
public sealed class ValueRefusedException(string reason) : Exception(reason)
{
    /// <summary>rw2's refusal sentence, e.g. "accept must not exceed review — write it as accept:review, e.g. 1.2:3.5."</summary>
    public string Reason { get; } = reason;

    // A body that is not a JSON string (plain text from an older endpoint) is taken as written.
    internal static async Task<ValueRefusedException> FromAsync(HttpResponseMessage res, CancellationToken ct)
    {
        var (raw, sentence) = await ServerSentence.ReadAsync(res, ct).ConfigureAwait(false);
        return new ValueRefusedException(sentence ?? raw);
    }
}
