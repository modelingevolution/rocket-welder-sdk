using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using ModelingEvolution.JsonParsableConverter;

namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Optimistic-concurrency token for a program tree — a content hash of the emitted
/// <c>.cs</c> (<c>"sha256:" + SHA256(...)</c>, per epic-035 design §2.2). Returned by
/// <see cref="IProgramsApi.GetTreeAsync"/> and echoed back in the <c>If-Match</c>
/// header of every edit; the server rejects a stale token with HTTP 409.
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<ProgramEtag>))]
public readonly record struct ProgramEtag : IParsable<ProgramEtag>
{
    private readonly string _value;

    public ProgramEtag(string value) =>
        _value = value ?? throw new ArgumentNullException(nameof(value));

    public static implicit operator string(ProgramEtag etag) => etag._value;
    public static explicit operator ProgramEtag(string value) => new(value);

    public static ProgramEtag Parse(string s, IFormatProvider? provider = null) =>
        TryParse(s, provider, out var result) ? result : throw new FormatException($"Invalid ProgramEtag: '{s}'.");

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ProgramEtag result)
    {
        if (string.IsNullOrEmpty(s))
        {
            result = default;
            return false;
        }

        result = new ProgramEtag(s);
        return true;
    }

    public override string ToString() => _value;
}
