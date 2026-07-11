using System.Text.Json;
using System.Text.Json.Serialization;
using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// Mutable ordered collection of named <see cref="Pose3{T}"/> entries. Not thread-safe.
/// Name lookup is case-sensitive and unique.
/// </summary>
public sealed class TeachingPointSet
{
    private readonly Dictionary<string, Pose3<double>> _poses = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();

    /// <summary>Number of teaching points.</summary>
    public int Count => _order.Count;

    /// <summary>Names in insertion order.</summary>
    public IReadOnlyList<string> Names => _order;

    /// <summary>Set (add or overwrite) a named pose.</summary>
    public void Set(string name, Pose3<double> pose)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!_poses.ContainsKey(name))
            _order.Add(name);
        _poses[name] = pose;
    }

    /// <summary>Get the pose for <paramref name="name"/>, or throw when absent.</summary>
    public Pose3<double> Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!_poses.TryGetValue(name, out var pose))
            throw new KeyNotFoundException($"Teaching point '{name}' not found.");
        return pose;
    }

    /// <summary>Try to get the pose for <paramref name="name"/>.</summary>
    public bool TryGet(string name, out Pose3<double> pose)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _poses.TryGetValue(name, out pose);
    }

    /// <summary>Remove a teaching point. Returns <c>true</c> if it existed.</summary>
    public bool Remove(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!_poses.Remove(name)) return false;
        _order.Remove(name);
        return true;
    }

    /// <summary>Whether the set contains <paramref name="name"/>.</summary>
    public bool Contains(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _poses.ContainsKey(name);
    }

    /// <summary>Enumerate entries in insertion order.</summary>
    public IEnumerable<KeyValuePair<string, Pose3<double>>> Enumerate()
    {
        foreach (var name in _order)
            yield return new KeyValuePair<string, Pose3<double>>(name, _poses[name]);
    }

    /// <summary>Serialize to JSON.</summary>
    public string ToJson(JsonSerializerOptions? options = null)
    {
        var points = new TeachingPointDto[_order.Count];
        for (int i = 0; i < _order.Count; i++)
        {
            var n = _order[i];
            points[i] = new TeachingPointDto { Name = n, Pose = _poses[n] };
        }
        var dto = new TeachingPointSetDto { Points = points };
        return JsonSerializer.Serialize(dto, options ?? DefaultJson);
    }

    /// <summary>Parse from JSON.</summary>
    public static TeachingPointSet FromJson(string json, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        var dto = JsonSerializer.Deserialize<TeachingPointSetDto>(json, options ?? DefaultJson)
                  ?? throw new InvalidOperationException("Teaching point set JSON deserialised to null.");
        var set = new TeachingPointSet();
        if (dto.Points is null) return set;
        foreach (var p in dto.Points)
        {
            if (string.IsNullOrEmpty(p.Name))
                throw new InvalidOperationException("Teaching point entry is missing 'name'.");
            set.Set(p.Name, p.Pose);
        }
        return set;
    }

    internal static readonly JsonSerializerOptions DefaultJson = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed class TeachingPointSetDto
    {
        [JsonPropertyName("points")]
        public TeachingPointDto[]? Points { get; set; }
    }

    private sealed class TeachingPointDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("pose")]
        public Pose3<double> Pose { get; set; }
    }
}
