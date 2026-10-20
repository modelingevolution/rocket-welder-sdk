namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// The value keys of one block (<c>/api/programs/{id}/blocks/{blockId}/values</c>, design.md §12.2). Block values
/// live in the program's <c>blocks/values.json</c>; writing them never recompiles the program and carries no etag.
/// </summary>
/// <param name="BlockId">The block the values belong to.</param>
/// <param name="BlockType">The block type, e.g. <c>Adapt</c>.</param>
/// <param name="Values">Every value key of the block's step type.</param>
public sealed record BlockValues(string BlockId, string BlockType, IReadOnlyList<EffectiveValue> Values)
{
    /// <summary>rw2's own text when the write landed but its history entry did not; <see langword="null"/> otherwise.</summary>
    public string? HistoryWarning { get; init; }
}
