namespace RocketWelder.SDK.Http.Programs;

/// <summary>One refused item of a bulk point write (epic-128 feature-001 design.md §1.1).</summary>
/// <param name="Name">The point name the item carried (empty when the item had none).</param>
/// <param name="Reason">rw2's sentence saying why the item was refused.</param>
public sealed record PointRefusal(string Name, string Reason);
