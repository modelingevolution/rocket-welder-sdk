using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// JSON converter factory for <see cref="ConfigProperty{T,TSelf}"/> derivatives.
///
/// <para>JSON format per item: <c>{ "Name": "Ip", "Value": "192.168.1.10" }</c></para>
///
/// <para>Call <see cref="ScanAssembly{TMarker}"/> at startup to register property types from
/// plugin assemblies so they can be round-tripped through JSON.</para>
/// </summary>
public class ConfigPropertyJsonConverter : JsonConverterFactory
{
    private static readonly Dictionary<string, Type> ByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registered prefix handlers for dynamic-named properties (e.g., "tag.").
    /// When a property name starts with a registered prefix the handler creates the
    /// appropriate <see cref="IConfigPropertyInstance"/> from the raw value.
    /// Thread-safe for concurrent registration.
    /// </summary>
    private static readonly ConcurrentBag<(string Prefix, Func<string, string, (string Key, IConfigPropertyInstance Instance)?> Handler)> PrefixHandlers = [];

    /// <summary>Registers a prefix handler for dynamic-named config properties.</summary>
    public static void RegisterPrefixHandler(string prefix, Func<string, string, (string Key, IConfigPropertyInstance Instance)?> handler)
        => PrefixHandlers.Add((prefix, handler));

    /// <summary>Scans <paramref name="assembly"/> for <c>ConfigProperty&lt;T,TSelf&gt;</c> subtypes and registers them by name.</summary>
    public static void ScanAssembly(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || !IsConfigPropertyType(type))
                continue;
            var name = ConfigProperty.GetName(type);
            ByName[name] = type;
        }
    }

    /// <summary>Scans the assembly containing <typeparamref name="TMarker"/>.</summary>
    public static void ScanAssembly<TMarker>() => ScanAssembly(typeof(TMarker).Assembly);

    internal static Type? GetRegisteredType(string name)
        => ByName.TryGetValue(name, out var type) ? type : null;

    /// <summary>
    /// Creates a config property instance from a name and raw string value using the registered
    /// type registry — the single source of truth for name+value → <see cref="IConfigPropertyInstance"/>.
    /// Returns <see langword="false"/> when the name is unknown or the value cannot be parsed.
    /// </summary>
    public static bool TryCreate(string name, string rawValue,
        [NotNullWhen(true)] out IConfigPropertyInstance? instance)
    {
        instance = null;
        if (string.IsNullOrEmpty(name)) return false;

        foreach (var (prefix, handler) in PrefixHandlers)
        {
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (handler(name, rawValue) is { } r) { instance = r.Instance; return true; }
        }

        var concreteType = GetRegisteredType(name);
        if (concreteType is null) return false;

        var valueType = GetValueType(concreteType);
        if (valueType is null) return false;
        if (!TryParseValue(valueType, rawValue, out var parsed)) return false;

        instance = (IConfigPropertyInstance)Activator.CreateInstance(concreteType, parsed)!;
        return true;
    }

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert) => IsConfigPropertyType(typeToConvert);

    /// <inheritdoc/>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(Inner<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private class Inner<TConcrete> : JsonConverter<TConcrete> where TConcrete : class
    {
        public override TConcrete? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => (TConcrete?)ReadItem(ref reader);

        public override void Write(Utf8JsonWriter writer, TConcrete value, JsonSerializerOptions options)
            => WriteItem(writer, value, options, explicitName: null);
    }

    /// <summary>Reads a single <c>{ "Name": "...", "Value": ... }</c> object.</summary>
    internal static object? ReadItem(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected StartObject");

        string? name = null;
        string? rawValue = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected PropertyName");

            var propertyName = reader.GetString()!;
            reader.Read();

            if (propertyName.Equals("Name", StringComparison.OrdinalIgnoreCase))
                name = reader.GetString();
            else if (propertyName.Equals("Value", StringComparison.OrdinalIgnoreCase))
            {
                rawValue = reader.TokenType switch
                {
                    JsonTokenType.String => reader.GetString(),
                    JsonTokenType.Number => reader.GetDecimal().ToString(),
                    JsonTokenType.True => "true",
                    JsonTokenType.False => "false",
                    _ => throw new JsonException($"Unexpected token type for Value: {reader.TokenType}")
                };
            }
        }

        if (name is null) throw new JsonException("Missing 'Name' property in ConfigProperty JSON");
        if (rawValue is null) throw new JsonException($"Missing 'Value' property for config property '{name}'");

        foreach (var (prefix, handler) in PrefixHandlers)
        {
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var result = handler(name, rawValue);
            if (result != null) return result;
        }

        var concreteType = GetRegisteredType(name)
            ?? throw new JsonException($"Unknown config property name: '{name}'");

        return CreateInstance(concreteType, rawValue);
    }

    /// <summary>Writes a single config property as <c>{ "Name": "...", "Value": ... }</c>.</summary>
    internal static void WriteItem(Utf8JsonWriter writer, object value, JsonSerializerOptions options, string? explicitName)
    {
        var type = value.GetType();
        writer.WriteStartObject();

        if (explicitName != null)
        {
            writer.WriteString("Name", explicitName);
            writer.WritePropertyName("Value");
            if (value is IConfigPropertyInstance inst)
                writer.WriteStringValue(inst.Value?.ToString());
            else
                writer.WriteNullValue();
        }
        else
        {
            writer.WriteString("Name", ConfigProperty.GetName(type));
            var valueProperty = GetValueProperty(type);
            if (valueProperty != null)
            {
                var val = valueProperty.GetValue(value);
                writer.WritePropertyName("Value");
                JsonSerializer.Serialize(writer, val, valueProperty.PropertyType, options);
            }
        }

        writer.WriteEndObject();
    }

    private static object CreateInstance(Type concreteType, string rawValue)
    {
        var valueType = GetValueType(concreteType)
            ?? throw new JsonException($"Type {concreteType.Name} does not derive from ConfigProperty<T,TSelf>");
        var parsedValue = ParseValue(valueType, rawValue);
        return Activator.CreateInstance(concreteType, parsedValue)!;
    }

    private static Type? GetValueType(Type concreteType)
    {
        var t = concreteType.BaseType;
        while (t != null && (!t.IsGenericType || t.GetGenericTypeDefinition() != typeof(ConfigProperty<,>)))
            t = t.BaseType;
        return t?.GetGenericArguments()[0];
    }

    private static readonly MethodInfo TryParseHelperMethod =
        typeof(ConfigPropertyJsonConverter).GetMethod(nameof(TryParseHelper), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static bool TryParseValue(Type valueType, string rawValue, out object? value)
    {
        if (valueType == typeof(string)) { value = rawValue; return true; }
        var generic = TryParseHelperMethod.MakeGenericMethod(valueType);
        var args = new object?[] { rawValue, null };
        var ok = (bool)generic.Invoke(null, args)!;
        value = ok ? args[1] : null;
        return ok;
    }

    private static bool TryParseHelper<T>(string value, out object? parsed) where T : IParsable<T>
    {
        if (T.TryParse(value, null, out var v) && v is not null) { parsed = v; return true; }
        parsed = null;
        return false;
    }

    private static readonly MethodInfo ParseHelperMethod =
        typeof(ConfigPropertyJsonConverter).GetMethod(nameof(ParseHelper), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static object ParseValue(Type valueType, string rawValue)
    {
        if (valueType == typeof(string)) return rawValue;
        var generic = ParseHelperMethod.MakeGenericMethod(valueType);
        return generic.Invoke(null, [rawValue])!;
    }

    private static T ParseHelper<T>(string value) where T : IParsable<T>
        => T.Parse(value, null);

    private static PropertyInfo? GetValueProperty(Type type)
    {
        var baseType = type;
        while (baseType != null)
        {
            if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(ConfigProperty<,>))
                return baseType.GetProperty("Value");
            baseType = baseType.BaseType;
        }
        return null;
    }

    internal static string? GetRegisteredTypeName(Type type)
    {
        foreach (var (name, registeredType) in ByName)
            if (registeredType == type) return name;
        return null;
    }

    private static bool IsConfigPropertyType(Type type)
    {
        var t = type.BaseType;
        while (t != null)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ConfigProperty<,>))
                return true;
            t = t.BaseType;
        }
        return false;
    }
}
