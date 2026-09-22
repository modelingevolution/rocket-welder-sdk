namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Thrown when <see cref="IProgramsApi.DeleteAsync"/> is refused because the program is
/// currently running (HTTP 409). Cancel the run first, then delete.
/// </summary>
public sealed class ProgramRunningException : Exception
{
    public ProgramRunningException(Guid programId, string? detail = null)
        : base($"Program '{programId}' is running and cannot be deleted (HTTP 409). Cancel it first.{Detail(detail)}")
    {
        ProgramId = programId;
    }

    /// <summary>The program whose delete was refused.</summary>
    public Guid ProgramId { get; }

    private static string Detail(string? detail) =>
        string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail}";
}
