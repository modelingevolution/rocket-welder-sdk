using System.Text.Json.Nodes;

namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Body of <c>PATCH /api/programs/{id}/blocks/{blockId}</c> — replace a block's
/// properties. The block's id and id-keyed references are preserved.
/// </summary>
/// <param name="Properties">The full replacement property set for the block.</param>
public sealed record EditBlockRequest(JsonObject Properties);
