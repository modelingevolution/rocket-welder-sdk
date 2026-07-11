using System.Text.Json;
using System.Text.Json.Serialization;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// JSON converter for <see cref="ConfigSet"/>: reads/writes an array of
/// <c>{ "Name": "...", "Value": ... }</c> objects.
/// </summary>
public class ConfigSetJsonConverter : JsonConverter<ConfigSet>
{
    /// <inheritdoc/>
    public override ConfigSet Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected StartArray");

        var set = new ConfigSet();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                break;

            var item = ConfigPropertyJsonConverter.ReadItem(ref reader);
            switch (item)
            {
                // Prefix handler returned (key, instance) tuple — use explicit key
                case ValueTuple<string, IConfigPropertyInstance> tuple:
                    set.Add(tuple.Item1, tuple.Item2);
                    break;
                // Standard typed property — derive key from type
                case IConfigPropertyInstance instance:
                    set.Add(ConfigProperty.GetName(instance.GetType()), instance);
                    break;
            }
        }

        return set;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ConfigSet value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        foreach (var (name, item) in value)
        {
            var typeName = ConfigPropertyJsonConverter.GetRegisteredTypeName(item.GetType());
            var explicitName = typeName != null && name.Equals(typeName, StringComparison.OrdinalIgnoreCase)
                ? null
                : name;
            ConfigPropertyJsonConverter.WriteItem(writer, item, options, explicitName);
        }

        writer.WriteEndArray();
    }
}
