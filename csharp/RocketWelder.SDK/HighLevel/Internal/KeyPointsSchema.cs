using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace RocketWelder.SDK.HighLevel.Internal;

/// <summary>
/// Implementation of <see cref="IKeyPointsSchema"/>.
/// </summary>
internal sealed class KeyPointsSchema : IKeyPointsSchema
{
    private readonly List<KeyPoint> _points = new();
    private int _nextId;

    public KeyPoint DefinePoint(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var point = new KeyPoint(_nextId++, name);
        _points.Add(point);
        return point;
    }

    public IReadOnlyList<KeyPoint> DefinedPoints => _points;

    public string GetMetadataJson()
    {
        var metadata = new
        {
            version = 1,
            type = "keypoints",
            points = _points.Select(p => new { id = p.Id, name = p.Name }).ToArray()
        };

        return JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
