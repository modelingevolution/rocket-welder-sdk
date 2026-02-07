using BlazorBlaze.Server;
using BlazorBlaze.VectorGraphics;
using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Graphics;

public enum PointSymbol
{
    Cross,
    Circle,
    Square
}

/// <summary>
/// Layer canvas adapter that accepts Drawing library primitives
/// and translates them to <see cref="ILayerCanvas"/> calls.
/// Extends <see cref="ILayerCanvas"/> so raw operations are also available.
/// </summary>
public interface IUiLayer : ILayerCanvas
{
    /// <summary>Draws a polygon, converting <see cref="Point{T}"/> to SKPoint.</summary>
    void Draw(Polygon<float> polygon);

    /// <summary>Draws a circle, converting center and radius to ints.</summary>
    void Draw(in Circle<float> circle);

    /// <summary>Draws a line segment between two points.</summary>
    void Draw(in Segment<float> segment);

    /// <summary>
    /// Draws an infinite line, clipped to the viewport rectangle.
    /// Does nothing if the line misses the viewport entirely.
    /// </summary>
    void Draw(in Line<float> line);

    /// <summary>Draws a rectangle, converting coordinates to ints.</summary>
    void Draw(in Rectangle<float> rectangle);

    /// <summary>Draws a triangle as a 3-point polygon.</summary>
    void Draw(in Triangle<float> triangle);

    /// <summary>Draws a point as a small circle marker.</summary>
    void Draw(in Point<float> point);

    /// <summary>
    /// Draws a labeled keypoint with a symbol marker.
    /// Saves/restores context so caller state is not affected.
    /// </summary>
    void DrawKeyPoint(string label, in Point<float> point,
        PointSymbol symbol = PointSymbol.Cross,
        RgbColor color = default,
        int size = 10,
        int fontSize = 12);
}
