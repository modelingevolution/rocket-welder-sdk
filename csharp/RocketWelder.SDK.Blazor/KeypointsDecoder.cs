using System;
using System.Collections.Generic;
using BlazorBlaze.VectorGraphics;
using BlazorBlaze.VectorGraphics.Protocol;
using RocketWelder.SDK.Protocols;

namespace RocketWelder.SDK.Blazor;

/// <summary>
/// WASM-side decoder for keypoint detection data with delta encoding support.
/// Renders keypoints as crosses with optional coordinate labels.
/// Protocol: [FrameType:1B][FrameId:8B][KeypointCount:varint][Keypoints...]
/// Master (0x00): [KeypointId:varint][X:int32][Y:int32][Confidence:uint16]
/// Delta (0x01): [KeypointId:varint][DeltaX:zigzag][DeltaY:zigzag][DeltaConf:zigzag]
/// </summary>
public class KeypointsDecoder : IFrameDecoder
{
    private readonly IStage _stage;
    private readonly byte _layerId;
    private readonly RgbColor _defaultColor;

    // Pre-allocated dictionaries for zero-allocation hot path
    private Dictionary<int, (int x, int y, ushort confidence)> _previousKeypoints;
    private Dictionary<int, (int x, int y, ushort confidence)> _currentKeypoints;

    /// <summary>
    /// Per-keypoint color mapping. If a keypoint ID is not in this dictionary,
    /// falls back to DefaultColor. Thread-safe for runtime modifications.
    /// </summary>
    public IDictionary<int, RgbColor> Brushes { get; } = new Dictionary<int, RgbColor>();

    /// <summary>
    /// Default color used when Brushes mapping doesn't contain the keypoint ID.
    /// </summary>
    public RgbColor DefaultColor => _defaultColor;

    /// <summary>
    /// When true, displays coordinate labels (x,y) next to each keypoint.
    /// Default: false.
    /// </summary>
    public bool ShowLabels { get; set; }

    /// <summary>
    /// Size of the cross marker in pixels (half-length of each arm).
    /// Default: 6.
    /// </summary>
    public int CrossSize { get; set; } = 6;

    /// <summary>
    /// Thickness of cross lines in pixels. Default: 2.
    /// </summary>
    public int Thickness { get; set; } = 2;

    /// <summary>
    /// Font size for coordinate labels. Default: 12.
    /// </summary>
    public int LabelFontSize { get; set; } = 12;

    public KeypointsDecoder(
        IStage stage,
        RgbColor? defaultColor = null,
        byte layerId = 0)
    {
        _stage = stage ?? throw new ArgumentNullException(nameof(stage));
        _defaultColor = defaultColor ?? new RgbColor(0, 255, 0); // Green default
        _layerId = layerId;

        // Pre-allocate with typical keypoint count (COCO has 17)
        _previousKeypoints = new Dictionary<int, (int x, int y, ushort confidence)>(32);
        _currentKeypoints = new Dictionary<int, (int x, int y, ushort confidence)>(32);
    }

    public DecodeResultV2 Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 10) // Minimum: 1B frameType + 8B frameId + 1B count
            return DecodeResultV2.NeedMoreData;

        try
        {
            var reader = new BinaryFrameReader(data);

            // Read frame type (master=0x00, delta=0x01)
            var frameType = reader.ReadByte();
            bool isDelta = frameType == 0x01;

            // Read FrameId (8B)
            var frameId = reader.ReadUInt64LE();

            // Read keypoint count
            var keypointCount = reader.ReadVarint();

            _stage.OnFrameStart(frameId);
            _stage.Clear(_layerId);

            var canvas = _stage[_layerId];

            // Clear and reuse current frame dictionary
            _currentKeypoints.Clear();

            for (uint i = 0; i < keypointCount; i++)
            {
                var keypointId = (int)reader.ReadVarint();
                int x, y;
                ushort confidence;

                if (isDelta && _previousKeypoints.Count > 0)
                {
                    // Delta frame: read deltas
                    var deltaX = reader.ReadZigZagVarint();
                    var deltaY = reader.ReadZigZagVarint();
                    var deltaConf = reader.ReadZigZagVarint();

                    if (_previousKeypoints.TryGetValue(keypointId, out var prev))
                    {
                        x = prev.x + deltaX;
                        y = prev.y + deltaY;
                        confidence = (ushort)(prev.confidence + deltaConf);
                    }
                    else
                    {
                        // New keypoint in delta frame - treat as absolute
                        x = deltaX;
                        y = deltaY;
                        confidence = (ushort)deltaConf;
                    }
                }
                else
                {
                    // Master frame: read absolute values
                    x = reader.ReadInt32LE();
                    y = reader.ReadInt32LE();
                    confidence = reader.ReadUInt16LE();
                }

                _currentKeypoints[keypointId] = (x, y, confidence);

                // Get color: check Brushes mapping first, then use default
                var color = Brushes.TryGetValue(keypointId, out var brushColor)
                    ? brushColor
                    : _defaultColor;

                // Draw cross marker
                canvas.DrawLine(x - CrossSize, y, x + CrossSize, y, color, Thickness);
                canvas.DrawLine(x, y - CrossSize, x, y + CrossSize, color, Thickness);

                // Draw coordinate label if enabled
                if (ShowLabels)
                {
                    canvas.DrawText($"({x},{y})", x + CrossSize + 2, y - 2, color, LabelFontSize);
                }
            }

            // Swap dictionaries for next frame (zero allocation)
            (_previousKeypoints, _currentKeypoints) = (_currentKeypoints, _previousKeypoints);

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
