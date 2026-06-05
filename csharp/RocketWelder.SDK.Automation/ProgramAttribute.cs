namespace RocketWelder.SDK.Automation;

/// <summary>
/// Marks a class as a program that can be discovered and executed by the automation system.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class ProgramAttribute : Attribute
{
    /// <summary>
    /// The display name of the program.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Optional description of what the program does.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Optional workspace to group related programs together.
    /// </summary>
    public string? Workspace { get; init; }

    /// <summary>
    /// When true, UI shows "Test Run" button for this program.
    /// </summary>
    public bool SupportsDryRun { get; init; } = false;

    /// <summary>
    /// Optional path to a JPEG/PNG preview image, relative to the project (.csproj) directory,
    /// shown on the program tile. Null = no image (a heuristic icon is shown instead).
    /// </summary>
    public string? Thumbnail { get; init; }

    public ProgramAttribute(string name) => Name = name ?? throw new ArgumentNullException(nameof(name));
}
