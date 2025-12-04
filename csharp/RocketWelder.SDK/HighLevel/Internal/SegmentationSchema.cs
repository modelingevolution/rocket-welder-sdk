using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace RocketWelder.SDK.HighLevel.Internal;

/// <summary>
/// Implementation of <see cref="ISegmentationSchema"/>.
/// </summary>
internal sealed class SegmentationSchema : ISegmentationSchema
{
    private readonly List<SegmentClass> _classes = new();

    public SegmentClass DefineClass(byte classId, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (_classes.Any(c => c.ClassId == classId))
            throw new ArgumentException($"Class ID {classId} is already defined", nameof(classId));

        var segmentClass = new SegmentClass(classId, name);
        _classes.Add(segmentClass);
        return segmentClass;
    }

    public IReadOnlyList<SegmentClass> DefinedClasses => _classes;

    public string GetMetadataJson()
    {
        var metadata = new
        {
            version = 1,
            type = "segmentation",
            classes = _classes.Select(c => new { classId = c.ClassId, name = c.Name }).ToArray()
        };

        return JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
