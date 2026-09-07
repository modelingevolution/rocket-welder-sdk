namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Thrown when an authoring edit targets a block that is not present in the program
/// (HTTP 404) — an unknown <see cref="BlockId"/>, or an unknown anchor reference on
/// an add. Distinct from <see cref="ProgramEtagMismatchException"/> (stale, 409).
/// </summary>
public sealed class BlockNotFoundException : Exception
{
    public BlockNotFoundException(Guid programId, BlockId? block)
        : base(block is { } b
            ? $"Block '{b}' was not found in program '{programId}' (HTTP 404)."
            : $"A referenced block was not found in program '{programId}' (HTTP 404).")
    {
        ProgramId = programId;
        Block = block;
    }

    /// <summary>The program the edit targeted.</summary>
    public Guid ProgramId { get; }

    /// <summary>The block that could not be resolved, when known.</summary>
    public BlockId? Block { get; }
}
