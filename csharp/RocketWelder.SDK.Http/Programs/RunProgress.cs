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
/// <param name="ArcOnSeconds">The sum over each ArcOn → ArcOff pair of ArcOff start − ArcOn end, in seconds.</param>
/// <param name="Blocks">Every block in program order.</param>
public sealed record RunProgress(
    Guid ProgramId,
    Guid RunId,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ProgramExecutionState>))] ProgramExecutionState State,
    bool DryRun,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    double? DurationSeconds,
    double ArcOnSeconds,
    IReadOnlyList<BlockProgress> Blocks);
