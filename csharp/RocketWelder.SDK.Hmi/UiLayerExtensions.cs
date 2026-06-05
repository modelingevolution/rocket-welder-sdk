using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Hmi;

/// <summary>
/// Extension methods for drawing <see cref="ModelingEvolution.Drawing"/> primitives
/// on <see cref="IUiLayer"/> that are not covered by the base interface.
/// </summary>
public static class UiLayerExtensions
{
    /// <summary>Draws a polyline as connected line segments.</summary>
    public static void Draw(this IUiLayer layer, Polyline<float> polyline)
    {
        var span = polyline.AsSpan();
        for (int i = 0; i < span.Length - 1; i++)
            layer.DrawLine(
                (int)span[i].X, (int)span[i].Y,
                (int)span[i + 1].X, (int)span[i + 1].Y);
    }

    /// <summary>Draws a cubic Bezier curve by densifying it to line segments.</summary>
    public static void Draw(this IUiLayer layer, BezierCurve<float> curve)
    {
        var points = curve.Densify().Span;
        for (int i = 0; i < points.Length - 1; i++)
            layer.DrawLine(
                (int)points[i].X, (int)points[i].Y,
                (int)points[i + 1].X, (int)points[i + 1].Y);
    }

    /// <summary>Draws a complex curve (mixed Bezier + polyline segments) by densifying.</summary>
    public static void Draw(this IUiLayer layer, ComplexCurve<float> curve)
    {
        layer.Draw(curve.Densify());
    }
}
