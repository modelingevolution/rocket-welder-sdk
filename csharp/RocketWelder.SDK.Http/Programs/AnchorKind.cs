namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Where a <see cref="BlockAnchor"/> places a block relative to an existing one.
/// The server resolves the anchor to a concrete index in the loaded tree.
/// </summary>
public enum AnchorKind
{
    /// <summary>Immediately after the referenced block.</summary>
    After,

    /// <summary>Immediately before the referenced block.</summary>
    Before,

    /// <summary>At the end of the tree; no reference block.</summary>
    Tail,
}
