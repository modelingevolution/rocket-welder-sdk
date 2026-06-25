using System.Collections;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// A keyed set of config properties. Lookup by concrete type or string name (case-insensitive).
///
/// <para>JSON format: array of <c>{ "Name": "...", "Value": ... }</c> objects.</para>
/// </summary>
[JsonConverter(typeof(ConfigSetJsonConverter))]
public class ConfigSet : IEnumerable<(string Name, IConfigPropertyInstance Value)>
{
    private readonly ConcurrentDictionary<string, IConfigPropertyInstance> _items = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>An empty config set (singleton, do not mutate).</summary>
    public static readonly ConfigSet Empty = new();

    /// <summary>Initializes an empty <see cref="ConfigSet"/>.</summary>
    public ConfigSet() { }

    /// <summary>Initializes a <see cref="ConfigSet"/> from a sequence of property instances.</summary>
    public ConfigSet(params IConfigPropertyInstance[] items)
    {
        foreach (var item in items)
        {
            var key = item is IConfigTypePropertyInstance dynamic
                ? dynamic.Name
                : ConfigProperty.GetName(item.GetType());
            _items[key] = item;
        }
    }

    /// <summary>Adds or replaces a property with an explicit key (for dynamic-named properties).</summary>
    public void Add(string key, IConfigPropertyInstance value) => _items[key] = value;

    /// <summary>Number of properties in this set.</summary>
    public int Count => _items.Count;

    /// <summary>Typed lookup: <c>set.Get&lt;IpProperty, Ipv4Address&gt;()</c> returns the IP value.</summary>
    public TValue? Get<TProperty, TValue>() where TProperty : IConfigProperty<TProperty>, IConfigPropertyInstance<TValue>
    {
        var tmp = _items.TryGetValue(TProperty.Name, out var item) ? item as IConfigPropertyInstance<TValue> : null;
        if (tmp != null) return tmp.Value;
        return default;
    }

    /// <summary>
    /// Returns a new <see cref="ConfigSet"/> with <paramref name="set"/> applied and
    /// <paramref name="unset"/> keys removed.
    /// </summary>
    public ConfigSet Apply(ConfigSet set, string[] unset)
    {
        var merged = new Dictionary<string, IConfigPropertyInstance>(_items, StringComparer.OrdinalIgnoreCase);
        foreach (var name in unset)
            merged.Remove(name);
        foreach (var (name, value) in set)
            merged[name] = value;
        return new ConfigSet([.. merged.Values]);
    }

    /// <inheritdoc/>
    public IEnumerator<(string Name, IConfigPropertyInstance Value)> GetEnumerator()
    {
        foreach (var kvp in _items)
            yield return (kvp.Key, kvp.Value);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
