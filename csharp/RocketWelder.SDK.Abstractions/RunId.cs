using System.Text.Json.Serialization;
using ModelingEvolution.JsonParsableConverter;

namespace RocketWelder.SDK.Abstractions;

/// <summary>
/// Identifier of a single program execution (run). Freshly minted when a program starts;
/// preserved across pause/stop within the same run. Used by the runtime to scope
/// <see cref="Lifetime.Run"/> data.
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<RunId>))]
public readonly record struct RunId(Guid Value) : IParsable<RunId>
{
    /// <summary>Mints a new <see cref="RunId"/>.</summary>
    public static RunId New() => new(Guid.NewGuid());

    public static implicit operator Guid(RunId id) => id.Value;
    public static implicit operator RunId(Guid value) => new(value);

    public static RunId Parse(string s, IFormatProvider? provider = null)
    {
        if (!TryParse(s, provider, out var result))
            throw new FormatException($"Invalid {nameof(RunId)}: {s}");
        return result;
    }

    public static bool TryParse(string? s, IFormatProvider? provider, out RunId result)
    {
        if (Guid.TryParse(s, out var guid))
        {
            result = new RunId(guid);
            return true;
        }
        result = default;
        return false;
    }

    public override string ToString() => Value.ToString();
}
