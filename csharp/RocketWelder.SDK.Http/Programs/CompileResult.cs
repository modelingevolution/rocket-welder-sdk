namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Result of <c>POST /api/repositories/{repoId}/compile</c>. The endpoint
/// blocks until <c>ProjectCompilationFinished</c> (or <c>Failed</c>) is observed.
/// </summary>
/// <param name="ProjectId">Stable id assigned to the .csproj that was compiled.</param>
/// <param name="Programs">All <c>IProgram</c> implementations discovered in the built assembly.</param>
/// <param name="Success">False when compilation failed; <see cref="Error"/> then carries the diagnostic.</param>
/// <param name="Error">Diagnostic text when <see cref="Success"/> is false.</param>
public sealed record CompileResult(
    Guid ProjectId,
    IReadOnlyList<ProgramInfo> Programs,
    bool Success,
    string? Error);
