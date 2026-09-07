namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Body of <c>POST /api/programs/{id}/blocks/{blockId}/move</c> — reposition a
/// block. Supply either an <see cref="Anchor"/> (relative to another block, or the
/// tail) or a signed <see cref="Delta"/> (steps up/down); exactly one is expected.
/// </summary>
/// <param name="Anchor">Target position relative to another block, or the tail.</param>
/// <param name="Delta">Signed number of positions to shift (negative = earlier).</param>
public sealed record MoveBlockRequest(
    BlockAnchor? Anchor,
    int? Delta);
