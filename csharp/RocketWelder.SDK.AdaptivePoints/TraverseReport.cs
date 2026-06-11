namespace RocketWelder.SDK.AdaptivePoints;

/// <summary>
/// Outcome of <see cref="IAdaptivePath.TraverseAsync"/>. Returned, never thrown — the program
/// inspects it and flags the pass done only when <see cref="Completed"/> is true (FR-2.5,
/// EC-2/EC-4). A move failure (joint limit, collision) or cancellation stops the traversal at the
/// point it reached and surfaces it in <see cref="StoppedAt"/>; that is the only intrinsic stop.
/// </summary>
/// <param name="Completed">Every taught point was reached. Only then is the pass done.</param>
/// <param name="StoppedAt">Name of the point a failed or cancelled move stopped on, or null when
/// the traversal completed. The un-flagged pass re-attempts on a later run.</param>
/// <param name="StartOffset">The start endpoint's offset state — resolved or zero.</param>
/// <param name="EndOffset">The end endpoint's offset state — resolved or zero.</param>
public sealed record TraverseReport(
    bool Completed,
    string? StoppedAt,
    OffsetState StartOffset,
    OffsetState EndOffset);
