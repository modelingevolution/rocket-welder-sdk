using System.Text.Json;
using System.Text.Json.Serialization;

namespace RocketWelder.SDK.Devices.Robot;

/// <summary>
/// The one JSON converter of <see cref="Velocity"/>: parse both, write one (docs/standards/dotnet.md § THE ONE SHAPE).
/// <list type="bullet">
/// <item>Reads the text form <c>"20%"</c>, <c>"100mm/s"</c>, <c>"50cm/min"</c>.</item>
/// <item>Reads the old object form <c>{"value":50,"unit":0}</c>, the unit a number (0 percent, 1 mm/s, 2 cm/min) or a
/// name (<c>"percent"</c>, <c>"percentage"</c>, <c>"mmPerSecond"</c>, <c>"cmPerMinute"</c>, any case). Member names are
/// read in any case.</item>
/// <item>Writes the text form, invariant culture.</item>
/// </list>
/// </summary>
public sealed class VelocityJsonConverter : JsonConverter<Velocity>
{
    public override Velocity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                var text = reader.GetString();
                return Velocity.TryParse(text, null, out var parsed)
                    ? parsed
                    : throw new JsonException($"Unknown velocity '{text}'. Expected '20%', '100mm/s' or '50cm/min'.");
            case JsonTokenType.StartObject:
                return ReadObject(ref reader);
            default:
                throw new JsonException(
                    $"A velocity is text ('20%', '100mm/s', '50cm/min') or {{\"value\":50,\"unit\":\"percent\"}}, not {reader.TokenType}.");
        }
    }

    private static Velocity ReadObject(ref Utf8JsonReader reader)
    {
        double? value = null;
        VelocityUnit? unit = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected a member name in a velocity object.");
            var name = reader.GetString();
            reader.Read();
            if (string.Equals(name, "value", StringComparison.OrdinalIgnoreCase))
                value = reader.TokenType == JsonTokenType.Number
                    ? reader.GetDouble()
                    : throw new JsonException("A velocity's 'value' must be a number.");
            else if (string.Equals(name, "unit", StringComparison.OrdinalIgnoreCase))
                unit = ReadUnit(ref reader);
            else
                reader.Skip();
        }

        if (value is not { } v) throw new JsonException("A velocity object needs 'value'.");
        if (unit is not { } u) throw new JsonException("A velocity object needs 'unit'.");
        return u switch
        {
            VelocityUnit.Percentage => Velocity.Percentage(v),
            VelocityUnit.MmPerSecond => Velocity.MmPerSecond(v),
            _ => Velocity.CmPerMinute(v)
        };
    }

    private static VelocityUnit ReadUnit(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return reader.TryGetInt32(out var n) && Enum.IsDefined((VelocityUnit)n)
                ? (VelocityUnit)n
                : throw new JsonException("A velocity unit number is 0 (percent), 1 (mm/s) or 2 (cm/min).");
        if (reader.TokenType == JsonTokenType.String)
            return reader.GetString()?.ToLowerInvariant() switch
            {
                "percent" or "percentage" => VelocityUnit.Percentage,
                "mmpersecond" => VelocityUnit.MmPerSecond,
                "cmperminute" => VelocityUnit.CmPerMinute,
                var other => throw new JsonException(
                    $"Unknown velocity unit '{other}'. Expected percent, mmPerSecond or cmPerMinute.")
            };
        throw new JsonException("A velocity's 'unit' is a number (0, 1, 2) or a name.");
    }

    /// <summary>Writes the text form: "20%", "100mm/s", "50cm/min".</summary>
    public override void Write(Utf8JsonWriter writer, Velocity value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
