namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// One block's progress in a run (epic-128 feature-001 design.md §2.6). Times are UTC, whole milliseconds, stamped on
/// the host.
/// </summary>
/// <param name="BlockId">The block id from the program tree.</param>
/// <param name="Type">The block type, e.g. <c>MovePtp</c>; empty when the block is no longer in the tree.</param>
/// <param name="Order">The block's position in program order, from 0.</param>
/// <param name="State">The block's state in the run.</param>
/// <param name="StartedAt">When the block first reported <c>Running</c>; <see langword="null"/> when it never did.</param>
/// <param name="EndedAt">When the block last ended; <see langword="null"/> when it has not ended.</param>
/// <param name="DurationSeconds"><paramref name="EndedAt"/> − <paramref name="StartedAt"/> in seconds; <see langword="null"/>
/// when either is missing.</param>
public sealed record BlockProgress(
    string BlockId,
    string Type,
    int Order,
    BlockRunState State,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    double? DurationSeconds);
