namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// Immutable configuration aggregating the collision dependencies used by
/// the collision detector and collision-aware robot moves.
/// </summary>
public sealed class CollisionEnvironment
{
    /// <summary>Environment geometry provider.</summary>
    public ICollisionSource Source { get; }

    /// <summary>Capsule radii (mm) for each of the six robot links. Indexed 0..5.</summary>
    public IReadOnlyList<double> LinkRadii { get; }

    /// <summary>Tool geometry attached to the robot flange.</summary>
    public ToolModel Tool { get; }

    /// <summary>Non-negative inflation (mm) applied to every bounding volume during checks.</summary>
    public double SafetyMargin { get; }

    public CollisionEnvironment(
        ICollisionSource source,
        IReadOnlyList<double> linkRadii,
        ToolModel tool,
        double safetyMargin = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(linkRadii);
        ArgumentNullException.ThrowIfNull(tool);

        if (linkRadii.Count != 6)
            throw new ArgumentException("LinkRadii must have exactly 6 entries.", nameof(linkRadii));

        for (int i = 0; i < 6; i++)
        {
            if (!(linkRadii[i] > 0))
                throw new ArgumentException($"LinkRadii[{i}] must be > 0; got {linkRadii[i]}.", nameof(linkRadii));
        }

        if (safetyMargin < 0)
            throw new ArgumentOutOfRangeException(nameof(safetyMargin), safetyMargin, "SafetyMargin must be non-negative.");

        Source = source;
        LinkRadii = linkRadii.ToArray();
        Tool = tool;
        SafetyMargin = safetyMargin;
    }
}
