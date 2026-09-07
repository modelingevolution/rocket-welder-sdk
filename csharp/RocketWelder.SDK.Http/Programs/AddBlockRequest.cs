using System.Text.Json.Nodes;

namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Body of <c>POST /api/programs/{id}/blocks</c> — insert a new block.
/// </summary>
/// <param name="Type">Block type to create (e.g. <c>Point</c>, <c>Move</c>, <c>ArcOn</c>).</param>
/// <param name="Properties">Initial type-specific properties.</param>
/// <param name="Anchor">Where to place the block relative to an existing one, or at the tail.</param>
public sealed record AddBlockRequest(
    string Type,
    JsonObject Properties,
    BlockAnchor Anchor);
