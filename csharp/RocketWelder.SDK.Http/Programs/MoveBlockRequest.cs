namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Body of <c>POST /api/programs/{id}/blocks/{blockId}/move</c> — reposition a
/// block. Supply <em>exactly one</em> target: an <see cref="Anchor"/> (relative to
/// another block, or the tail) or a signed <see cref="Delta"/> (steps up/down). The
/// constructor rejects "neither" and "both"; only the supplied field is serialized.
/// </summary>
public sealed record MoveBlockRequest
{
    public MoveBlockRequest(BlockAnchor? anchor, int? delta)
    {
        if (anchor is null == delta is null)
            throw new ArgumentException("Specify exactly one of anchor or delta.", nameof(anchor));

        Anchor = anchor;
        Delta = delta;
    }

    /// <summary>Target position relative to another block, or the tail.</summary>
    public BlockAnchor? Anchor { get; }

    /// <summary>Signed number of positions to shift (negative = earlier).</summary>
    public int? Delta { get; }
}
