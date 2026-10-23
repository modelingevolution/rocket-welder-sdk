using System.Text.Json.Serialization;

namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// The state of one block in a run (epic-128 feature-001 design.md §2.6, ADR-7). Written as a string on the wire.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<BlockRunState>))]
public enum BlockRunState
{
    /// <summary>The run is going and has not reached the block yet.</summary>
    Pending,

    /// <summary>The block is executing now.</summary>
    Running,

    /// <summary>The block completed.</summary>
    Done,

    /// <summary>The block faulted.</summary>
    Failed,

    /// <summary>The run stopped while the block was working.</summary>
    Interrupted,

    /// <summary>The run ended without reaching the block.</summary>
    NotRun,
}
