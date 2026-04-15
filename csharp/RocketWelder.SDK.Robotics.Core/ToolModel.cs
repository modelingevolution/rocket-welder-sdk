namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// Abstract tool geometry attached to the robot flange. Immutable.
/// Resolved by the active <see cref="ICollisionSource"/>.
/// </summary>
public abstract record ToolModel
{
    /// <summary>Protected constructor — use <see cref="CapsuleToolModel"/> or <see cref="MeshToolModel"/>.</summary>
    protected ToolModel() { }

    /// <summary>The zero-tool sentinel (no geometry attached).</summary>
    public static readonly ToolModel None = new NoneToolModel();

    private sealed record NoneToolModel : ToolModel;
}

/// <summary>
/// Capsule tool geometry: a swept sphere along a line segment in the flange frame (millimetres).
/// Planning-grade check for <see cref="PrimitiveCollisionSource"/>.
/// </summary>
/// <param name="Length">Length of the capsule axis in mm. Must be non-negative.</param>
/// <param name="Radius">Radius in mm. Must be positive.</param>
public sealed record CapsuleToolModel(double Length, double Radius) : ToolModel
{
    /// <inheritdoc />
    public bool Equals(CapsuleToolModel? other) =>
        other is not null && Length == other.Length && Radius == other.Radius;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Length, Radius);
}

/// <summary>
/// Mesh tool reference. The mesh itself is owned by a mesh-capable <see cref="ICollisionSource"/> which
/// resolves <see cref="Id"/> at query time.
/// </summary>
/// <param name="Id">Opaque mesh identifier (e.g., asset name or hash).</param>
public sealed record MeshToolModel(string Id) : ToolModel
{
    /// <inheritdoc />
    public bool Equals(MeshToolModel? other) =>
        other is not null && Id == other.Id;

    /// <inheritdoc />
    public override int GetHashCode() => Id.GetHashCode();
}
