namespace RocketWelder.SDK.Http.Station;

/// <summary>
/// One station Programming default (<c>/api/station/programming-defaults</c>, epic-029 feature-011, design.md §12.4):
/// an index entry with what the station stores and what applies. Bands are canonical <c>"accept:review"</c> text in mm.
/// </summary>
/// <param name="Key">The index key <c>&lt;Kind&gt;.&lt;Property&gt;</c>, e.g. <c>Adaptation.PathTolerance</c>.</param>
/// <param name="Kind">The kind the key belongs to, e.g. <c>Adaptation</c>.</param>
/// <param name="BlockTypes">The block types the value applies to, e.g. <c>Adapt</c>, <c>AutoAdapt</c>.</param>
/// <param name="Control">The editor control for the value, e.g. <c>ToleranceBandField</c>.</param>
/// <param name="Stored">The text the station stores; <see langword="null"/> when unset (the built-in applies).</param>
/// <param name="Effective">The value that applies, canonical text.</param>
/// <param name="Layer">Where <paramref name="Effective"/> comes from: <c>station</c> or <c>builtIn</c>.</param>
/// <param name="Warning">The refusal sentence when <paramref name="Stored"/> is unreadable; otherwise <see langword="null"/>.</param>
public sealed record StationDefault(
    string Key,
    string Kind,
    IReadOnlyList<string> BlockTypes,
    string Control,
    string? Stored,
    string Effective,
    string Layer,
    string? Warning)
{
    /// <summary>A cell value's source text: where the value comes from (who measured or ruled it, when). Present on station-only cell
    /// values (<see cref="StationOnly"/>); absent on block-property defaults. Optional: older stations do not send it.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; init; }

    /// <summary>True for a cell value (kind <c>Cell</c>, e.g. <c>Cell.HoldPosture</c>): the station's only, never copied into a program, and
    /// needs its <see cref="Source"/> when set. False for a block-property default.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool StationOnly { get; init; }
}
