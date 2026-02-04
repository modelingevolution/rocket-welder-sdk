using System;
using System.Collections.Generic;
using BlazorBlaze.VectorGraphics;
using BlazorBlaze.VectorGraphics.Protocol;
using RocketWelder.SDK.Protocols;
using SkiaSharp;

namespace RocketWelder.SDK.Blazor;

/// <summary>
/// WASM-side decoder for segmentation polygon data.
/// Protocol: [FrameId:8B][Width:varint][Height:varint][Instances...]
/// Instance: [ClassId:1B][InstanceId:1B][Confidence:2B LE][PointCount:varint][Points:zigzag+delta]
/// </summary>
public class SegmentationDecoder : IFrameDecoder
{
    private readonly IStage _stage;
    private readonly byte _layerId;
    private readonly RgbColor _defaultColor;

    // Pre-allocated point buffer to avoid allocations in hot path
    private SKPoint[] _pointBuffer;
    private const int InitialBufferSize = 256;

    /// <summary>
    /// Per-class color mapping. If a class ID is not in this dictionary,
    /// falls back to DefaultColor. Thread-safe for runtime modifications.
    /// </summary>
    public IDictionary<byte, RgbColor> Brushes { get; } = new Dictionary<byte, RgbColor>();

    /// <summary>
    /// Default color used when Brushes mapping doesn't contain the class ID.
    /// </summary>
    public RgbColor DefaultColor => _defaultColor;

    /// <summary>
    /// Thickness of polygon stroke lines in pixels. Default: 2.
    /// </summary>
    public int Thickness { get; set; } = 2;

    /// <summary>
    /// When true, displays the class ID (or name from <see cref="ClassNames"/>) next to each polygon.
    /// </summary>
    public bool ShowClassId { get; set; }

    /// <summary>
    /// When true, displays the instance ID next to each polygon.
    /// </summary>
    public bool ShowInstanceId { get; set; }

    /// <summary>
    /// When true, displays the confidence percentage next to each polygon.
    /// </summary>
    public bool ShowConfidence { get; set; }

    /// <summary>
    /// Font size for labels. Default: 12.
    /// </summary>
    public int LabelFontSize { get; set; } = 12;

    /// <summary>
    /// Optional class ID to human-readable name mapping.
    /// When a class ID is found in this dictionary, the name is displayed instead of the numeric ID.
    /// </summary>
    public IDictionary<byte, string> ClassNames { get; set; } = new Dictionary<byte, string>();

    public SegmentationDecoder(
        IStage stage,
        RgbColor? defaultColor = null,
        byte layerId = 0)
    {
        _stage = stage ?? throw new ArgumentNullException(nameof(stage));
        _defaultColor = defaultColor ?? new RgbColor(255, 100, 100); // Red default
        _layerId = layerId;

        // Pre-allocate buffer for polygon points
        _pointBuffer = new SKPoint[InitialBufferSize];
    }

    private string BuildLabel(byte classId, byte instanceId, ushort confidence)
    {
        Span<char> buf = stackalloc char[64];
        int pos = 0;

        if (ShowClassId)
        {
            if (ClassNames.TryGetValue(classId, out var name))
            {
                name.AsSpan().CopyTo(buf[pos..]);
                pos += name.Length;
            }
            else
            {
                classId.TryFormat(buf[pos..], out var written);
                pos += written;
            }
        }

        if (ShowInstanceId)
        {
            if (pos > 0) buf[pos++] = ' ';
            buf[pos++] = '#';
            instanceId.TryFormat(buf[pos..], out var written);
            pos += written;
        }

        if (ShowConfidence)
        {
            if (pos > 0) buf[pos++] = ' ';
            var pct = confidence / 65535f * 100f;
            pct.TryFormat(buf[pos..], out var written, "0.0");
            pos += written;
            buf[pos++] = '%';
        }

        return buf[..pos].ToString();
    }

    public DecodeResultV2 Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 9) // Minimum: 8 bytes frameId + 1 byte data
            return DecodeResultV2.NeedMoreData;

        try
        {
            var reader = new BinaryFrameReader(data);

            // Read header: FrameId (8B), Width (varint), Height (varint)
            var frameId = reader.ReadUInt64LE();
            var width = reader.ReadVarint();
            var height = reader.ReadVarint();

            _stage.OnFrameStart(frameId);
            _stage.Clear(_layerId);

            var canvas = _stage[_layerId];

            // Read instances until end of data
            while (reader.HasMore)
            {
                var classId = reader.ReadByte();
                var instanceId = reader.ReadByte();
                var confidence = reader.ReadUInt16LE();  // confidence (0-65535), not used for rendering
                var pointCount = (int)reader.ReadVarint();

                if (pointCount == 0)
                    continue;

                // Ensure buffer is large enough
                if (_pointBuffer.Length < pointCount)
                {
                    // Grow buffer (rare - only if polygon has more points than buffer)
                    _pointBuffer = new SKPoint[Math.Max(pointCount, _pointBuffer.Length * 2)];
                }

                // First point: absolute (zigzag encoded)
                int x = reader.ReadZigZagVarint();
                int y = reader.ReadZigZagVarint();
                _pointBuffer[0] = new SKPoint(x, y);

                // Remaining points: deltas
                for (int i = 1; i < pointCount; i++)
                {
                    x += reader.ReadZigZagVarint();
                    y += reader.ReadZigZagVarint();
                    _pointBuffer[i] = new SKPoint(x, y);
                }

                // Get color: check Brushes mapping first, then use default
                var color = Brushes.TryGetValue(classId, out var brushColor)
                    ? brushColor
                    : _defaultColor;

                // Draw polygon using pre-allocated buffer slice (no ToArray allocation)
                canvas.DrawPolygon(_pointBuffer.AsSpan(0, pointCount), color, Thickness);

                // Draw labels at polygon centroid if any label flags are enabled
                if (ShowClassId || ShowInstanceId || ShowConfidence)
                {
                    float cx = 0, cy = 0;
                    for (int i = 0; i < pointCount; i++)
                    {
                        cx += _pointBuffer[i].X;
                        cy += _pointBuffer[i].Y;
                    }
                    cx /= pointCount;
                    cy /= pointCount;

                    var label = BuildLabel(classId, instanceId, confidence);
                    canvas.DrawText(label, (int)cx, (int)cy, color, LabelFontSize);
                }
            }

            _stage.OnFrameEnd();
            return DecodeResultV2.Ok(data.Length, frameId, layerCount: 1);
        }
        catch
        {
            // Decoder errors are non-fatal - return NeedMoreData to skip malformed frames
            return DecodeResultV2.NeedMoreData;
        }
    }
}
