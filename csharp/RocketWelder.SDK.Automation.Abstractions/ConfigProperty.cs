using ModelingEvolution.Ipv4;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// Static helpers for config property name lookup and type creation.
/// </summary>
public static class ConfigProperty
{
    private static readonly ConcurrentDictionary<Type, string> _cache = new();

    /// <summary>Returns the static <c>Name</c> for a typed config property.</summary>
    public static string GetName<T>() where T : IConfigProperty<T>, IConfigPropertyInstance => T.Name;

    /// <summary>
    /// The key an INSTANCE is stored under: its own <see cref="IConfigTypePropertyInstance.Name"/> when it is
    /// dynamically keyed, otherwise its type's static name. The one statement of that rule — the set, its JSON
    /// converter and the item writer all key through here, so a dynamically keyed item cannot be handled by one
    /// of them and thrown on by another.
    /// </summary>
    public static string GetName(IConfigPropertyInstance instance) =>
        instance is IConfigTypePropertyInstance dynamic ? dynamic.Name : GetName(instance.GetType());

    /// <summary>Returns the static <c>Name</c> for a config property type resolved at runtime.</summary>
    public static string GetName(Type t)
    {
        return _cache.GetOrAdd(t, static type =>
        {
            var method = typeof(ConfigProperty)
                .GetMethod(nameof(GetName), 1, Type.EmptyTypes)!
                .MakeGenericMethod(type);
            return (string)method.Invoke(null, null)!;
        });
    }
}

/// <summary>Static name contract for a typed config property.</summary>
public interface IConfigProperty<TSelf> where TSelf : IConfigProperty<TSelf>, IConfigPropertyInstance
{
    static abstract string Name { get; }
}

/// <summary>Non-generic instance of a config property (key+value pair in a <see cref="ConfigSet"/>).</summary>
public interface IConfigPropertyInstance
{
    object Value { get; }
}

/// <summary>A config property with a dynamic (instance-level) key, e.g., "tag.axis.speed".</summary>
public interface IConfigTypePropertyInstance : IConfigPropertyInstance
{
    string Name { get; }
}

/// <summary>Typed config property instance.</summary>
public interface IConfigPropertyInstance<T> : IConfigPropertyInstance
{
    new T Value { get; }
}

/// <summary>
/// Generic base for typed configuration properties.
/// <typeparamref name="T"/> must be <see cref="IParsable{TSelf}"/> so values can be deserialized.
/// </summary>
[JsonConverter(typeof(ConfigPropertyJsonConverter))]
public abstract record ConfigProperty<T, TSelf>(T Value) : IConfigPropertyInstance<T>
    where T : IParsable<T>
    where TSelf : IConfigProperty<TSelf>, IConfigPropertyInstance<T>
{
    /// <inheritdoc/>
    public override string ToString() => $"{TSelf.Name}={Value}";
    object IConfigPropertyInstance.Value => Value!;
}

// ─────────────────────────────────────────────────────────────────────────────
//  Built-in concrete property types (device-domain-neutral infrastructure)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>IPv4 address (e.g., for <c>IpProperty</c> / <c>IRobot.Address</c> host).</summary>
[JsonConverter(typeof(ConfigPropertyJsonConverter))]
/// <summary>
/// A config item whose <c>Name</c> this build does not recognise, kept VERBATIM instead of rejected.
///
/// <para>
/// Config property names are contributed by whoever owns them — the host, an adapter, or a plugin — so a
/// store written by a cell with one plugin set is routinely read by a process with another. Historic events
/// also outlive renames. Deserialization must therefore be TOTAL: the previous behaviour threw
/// <see cref="System.Text.Json.JsonException"/> on an unknown name, and because the throw happened inside a
/// read model's subscription it killed that subscription, which retried every 30 s on the same event and
/// never caught up again — one unrecognised string permanently blinded the devices page, with nothing in
/// the UI to say why.
/// </para>
///
/// <para>
/// Carrying <see cref="Name"/> at runtime makes it round-trip: <c>ConfigSet</c> already keys
/// <see cref="IConfigTypePropertyInstance"/> items by their own name, so an unknown property is written
/// back exactly as it arrived and is not silently dropped on the next save.
/// </para>
/// </summary>
public sealed record UnrecognizedProperty(string Name, string Value)
    : IConfigTypePropertyInstance, IConfigPropertyInstance<string>
{
    string IConfigPropertyInstance<string>.Value => Value;
    object IConfigPropertyInstance.Value => Value;
}

public record IpProperty(Ipv4Address Value) : ConfigProperty<Ipv4Address, IpProperty>(Value), IConfigProperty<IpProperty>
{
    public static string Name => "Ip";
}

/// <summary>Network port number.</summary>
[JsonConverter(typeof(ConfigPropertyJsonConverter))]
public record PortProperty(int Value) : ConfigProperty<int, PortProperty>(Value), IConfigProperty<PortProperty>
{
    public static string Name => "Port";
}

/// <summary>Serial number for device identification (e.g., camera serial).</summary>
[JsonConverter(typeof(ConfigPropertyJsonConverter))]
public record SerialNumberProperty(string Value) : ConfigProperty<string, SerialNumberProperty>(Value), IConfigProperty<SerialNumberProperty>
{
    public static string Name => "SerialNumber";
}

/// <summary>MJPEG-over-HTTP source URL for a camera device.</summary>
[JsonConverter(typeof(ConfigPropertyJsonConverter))]
public record MjpegUrlProperty(string Value) : ConfigProperty<string, MjpegUrlProperty>(Value), IConfigProperty<MjpegUrlProperty>
{
    public static string Name => "MjpegUrl";
}

/// <summary>URL or <c>file://</c> path returning a JSON intrinsics document for a camera.</summary>
[JsonConverter(typeof(ConfigPropertyJsonConverter))]
public record IntrinsicsUrlProperty(string Value) : ConfigProperty<string, IntrinsicsUrlProperty>(Value), IConfigProperty<IntrinsicsUrlProperty>
{
    public static string Name => "IntrinsicsUrl";
}

/// <summary>1-D distance sensor sweetspot / mid-range distance in mm.</summary>
[JsonConverter(typeof(ConfigPropertyJsonConverter))]
public record TargetDistanceMmProperty(double Value) : ConfigProperty<double, TargetDistanceMmProperty>(Value), IConfigProperty<TargetDistanceMmProperty>
{
    public static string Name => "TargetDistanceMm";
}

/// <summary>Lower bound of the in-range window as a signed offset from the sweetspot (mm).</summary>
[JsonConverter(typeof(ConfigPropertyJsonConverter))]
public record MinOffsetMmProperty(double Value) : ConfigProperty<double, MinOffsetMmProperty>(Value), IConfigProperty<MinOffsetMmProperty>
{
    public static string Name => "MinOffsetMm";
}

/// <summary>Upper bound of the in-range window as a signed offset from the sweetspot (mm).</summary>
[JsonConverter(typeof(ConfigPropertyJsonConverter))]
public record MaxOffsetMmProperty(double Value) : ConfigProperty<double, MaxOffsetMmProperty>(Value), IConfigProperty<MaxOffsetMmProperty>
{
    public static string Name => "MaxOffsetMm";
}

/// <summary>
/// Maximum Cartesian TCP line speed (mm/s). Robot-model specific;
/// required for any robot that must honour an absolute weld speed.
/// </summary>
[JsonConverter(typeof(ConfigPropertyJsonConverter))]
public record MaxLinearSpeedMmPerSecondProperty(double Value) : ConfigProperty<double, MaxLinearSpeedMmPerSecondProperty>(Value), IConfigProperty<MaxLinearSpeedMmPerSecondProperty>
{
    public static string Name => "MaxLinearSpeedMmPerSecond";
}
