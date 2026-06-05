using System.Text.Json.Serialization;
using ModelingEvolution.JsonParsableConverter;

namespace RocketWelder.SDK.Automation.WeldProgramModel;

/// <summary>
/// Identifies a seam type (e.g. "A4"). Resolves welding parameters — current, voltage,
/// weave, travel speed — through a catalogue. The catalogue itself is OQ-2 (does it live
/// here or in the welding epic). For now this is a value identifier only.
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<SeamType>))]
public readonly record struct SeamType(string Code) : IParsable<SeamType>
{
    public static implicit operator string(SeamType seam) => seam.Code;
    public static implicit operator SeamType(string code) => new(code);

    public static SeamType Parse(string s, IFormatProvider? provider = null)
    {
        if (!TryParse(s, provider, out var result))
            throw new FormatException($"Invalid {nameof(SeamType)}: {s}");
        return result;
    }

    public static bool TryParse(string? s, IFormatProvider? provider, out SeamType result)
    {
        if (!string.IsNullOrWhiteSpace(s))
        {
            result = new SeamType(s);
            return true;
        }
        result = default;
        return false;
    }

    public override string ToString() => Code;
}
