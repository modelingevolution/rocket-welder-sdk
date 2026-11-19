using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using ModelingEvolution.JsonParsableConverter;

namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// The name a taught point is declared and referenced under in a program (<c>p1</c>, <c>t2</c>) — a natural key, so no
/// prefix; on the wire the name itself, also as a dictionary key (<c>{"p1":"t1"}</c>). Used by the members added for
/// rocket-welder2 epic-150 (<see cref="BlockEditResult.Points"/>); the SDK's existing point-name members stay <c>string</c>.
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<PointName>))]
public readonly record struct PointName : IParsable<PointName>
{
    private readonly string _value;

    public PointName(string value) =>
        _value = value ?? throw new ArgumentNullException(nameof(value));

    public static implicit operator string(PointName name) => name._value;
    public static explicit operator PointName(string value) => new(value);

    public static PointName Parse(string s, IFormatProvider? provider = null) =>
        TryParse(s, provider, out var result) ? result : throw new FormatException($"Invalid PointName: '{s}'.");

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out PointName result)
    {
        if (string.IsNullOrEmpty(s))
        {
            result = default;
            return false;
        }

        result = new PointName(s);
        return true;
    }

    public override string ToString() => _value;
}
