namespace RocketWelder.SDK.Http.Repositories;

/// <summary>
/// Wire shape of <c>POST /api/repositories/{id}/programs</c>
/// (<c>{ "programId": "..." }</c>). Unwrapped to a bare <see cref="Guid"/> by the SDK.
/// </summary>
internal sealed record CreateProgramResult(Guid ProgramId);
