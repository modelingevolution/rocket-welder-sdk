using System.Text.Json.Serialization;

namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// A run's progress (<c>GET /api/programs/{id}/runs/{runId}/progress</c>, epic-128 feature-001 design.md §2.6). Only the
/// program's latest run is answerable (ADR-5).
/// </summary>
/// <param name="ProgramId">The program.</param>
/// <param name="RunId">The run.</param>
/// <param name="State">The run's state: <c>Running</c>, <c>Finished</c>, <c>Failed</c> or <c>Cancelled</c>. Written as a
/// string, as the design's wire example shows; an ordinal is read too.</param>
/// <param name="DryRun">Whether the run is a dry run.</param>
/// <param name="StartedAt">When the run started (UTC, ms).</param>
/// <param name="EndedAt">When the run ended; <see langword="null"/> while it runs.</param>
/// <param name="DurationSeconds"><paramref name="EndedAt"/> − <paramref name="StartedAt"/> in seconds; <see langword="null"/>
/// while it runs.</param>
/// <param name="ArcOnSeconds">The sum over each ArcOn → ArcOff pair of ArcOff start − ArcOn end, in seconds. An ArcOn
/// whose ArcOff never started counts until the run ended, or until now while the run is running (ADR-8).
/// <see langword="null"/> (ABSENT on the wire) when the server cannot give a true figure; <see cref="ArcOnSecondsUnknown"/>
/// then says why. An unknown time is never sent as 0. A client from before this field was nullable reads an absent value
/// as 0 until it is updated; it does not fail.</param>
/// <param name="Blocks">Every block in program order.</param>
public sealed record RunProgress(
    Guid ProgramId,
    Guid RunId,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ProgramExecutionState>))] ProgramExecutionState State,
    bool DryRun,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    double? DurationSeconds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? ArcOnSeconds,
    IReadOnlyList<BlockProgress> Blocks)
{
    /// <summary>
    /// Why <see cref="ArcOnSeconds"/> is absent: a stable key, null (absent on the wire) whenever the figure is given.
    /// <c>"recorded-run-shared-body"</c>: answered from the recorded run (after a host restart) for a program whose weld
    /// path runs in a shared body, so the per-pass times are not kept. <c>"unpaired-passes"</c>: a weld path's ArcOn and
    /// ArcOff ran a different number of times (an ArcOn inside a shared body with its ArcOff outside it).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ArcOnSecondsUnknown { get; init; }
}
