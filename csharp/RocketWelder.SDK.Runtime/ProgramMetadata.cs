namespace RocketWelder.SDK.Runtime;

/// <summary>
/// Metadata extracted from a program's [Program] attribute.
/// </summary>
public record ProgramMetadata(
    string Name,
    string? Description,
    Workspace? Workspace,
    bool SupportsDryRun
);
