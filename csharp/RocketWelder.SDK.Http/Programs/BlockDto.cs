using System.Text.Json.Nodes;

namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// One block in a program tree. Backs a row of <c>GET /api/programs/{id}/tree</c>.
/// </summary>
/// <param name="BlockId">Stable id used to address this block in edits.</param>
/// <param name="Type">Block type discriminator (e.g. <c>Point</c>, <c>Move</c>, <c>ArcOn</c>).</param>
/// <param name="Properties">Type-specific properties as an open JSON object.</param>
/// <param name="Disabled">True when the block is present but excluded from execution.</param>
public sealed record BlockDto(
    BlockId BlockId,
    string Type,
    JsonObject Properties,
    bool Disabled);
