using System;
using BlazorBlaze.VectorGraphics;

namespace RocketWelder.SDK.Blazor;

/// <summary>
/// Color palette for mapping class/keypoint IDs to colors.
/// Used by decoders to render segmentation polygons and keypoints.
/// Uses RgbColor because BlazorBlaze canvas APIs require it.
/// </summary>
public class ColorPalette
{
    private readonly RgbColor[] _colors;

    /// <summary>
    /// Default palette with 16 distinct colors.
    /// </summary>
    public static ColorPalette Default { get; } = new(new RgbColor[]
    {
        new(255, 100, 100),  // Red
        new(100, 255, 100),  // Green
        new(100, 100, 255),  // Blue
        new(255, 255, 100),  // Yellow
        new(255, 100, 255),  // Magenta
        new(100, 255, 255),  // Cyan
        new(255, 165, 0),    // Orange
        new(128, 0, 128),    // Purple
        new(255, 192, 203),  // Pink
        new(0, 128, 128),    // Teal
        new(165, 42, 42),    // Brown
        new(128, 128, 0),    // Olive
        new(255, 127, 80),   // Coral
        new(70, 130, 180),   // Steel Blue
        new(144, 238, 144),  // Light Green
        new(221, 160, 221),  // Plum
    });

    public ColorPalette(RgbColor[] colors)
    {
        _colors = colors ?? throw new ArgumentNullException(nameof(colors));
        if (_colors.Length == 0)
            throw new ArgumentException("Palette must have at least one color", nameof(colors));
    }

    /// <summary>
    /// Gets color for the specified ID. Wraps around if ID exceeds palette size.
    /// </summary>
    public RgbColor this[int id] => _colors[id % _colors.Length];

    /// <summary>
    /// Number of colors in the palette.
    /// </summary>
    public int Count => _colors.Length;
}
