namespace RocketWelder.SDK.Operations;

/// <summary>
/// The git-stored weld program artifact — one file per part type (per <c>data-model.md</c> §1/§2).
/// Owns part-level data: the STEP ref, the preview, the program-level <see cref="Datum"/>,
/// the ordered <see cref="Segments"/> list, and version metadata.
/// All lengths in millimetres, all angles in degrees, model frame = the STEP file's own coordinate system.
/// </summary>
/// <param name="Id">Stable WeldProgram GUID (survives file rename).</param>
/// <param name="Name">Human-readable program name.</param>
/// <param name="Step">Content-pinned STEP reference (LFS object).</param>
/// <param name="Preview">Preview JPG reference (LFS).</param>
/// <param name="Datum">Program-level registration reference (2–3 points).</param>
/// <param name="Segments">
/// The segments in <em>authored weld order</em>. The array index IS the weld sequence; the
/// serializer never reorders for any other reason (§2 rule 2).
/// </param>
/// <param name="WeldOrderStrategy">
/// The heuristic that produced the segment order (informational), e.g. <c>"distortion-balanced"</c>.
/// </param>
/// <param name="Version">Per-save metadata; the git history is the real version log.</param>
public sealed record WeldProgram(
    Guid Id,
    string Name,
    StepRef Step,
    PreviewRef Preview,
    Datum Datum,
    IReadOnlyList<Segment> Segments,
    string WeldOrderStrategy,
    VersionInfo Version);

/// <summary>
/// Content-pinned STEP reference (per <c>data-model.md</c> §2 <c>step</c>).
/// </summary>
/// <param name="Path">Path to the STEP file, relative to the program file.</param>
/// <param name="Sha256">SHA-256 of the STEP content (LFS pin).</param>
public sealed record StepRef(string Path, string Sha256);

/// <summary>
/// Preview image reference (per <c>data-model.md</c> §2 <c>preview</c>).
/// </summary>
/// <param name="Path">Path to the preview JPG, relative to the program file.</param>
public sealed record PreviewRef(string Path);

/// <summary>
/// Per-save version metadata (per <c>data-model.md</c> §2 <c>version</c>).
/// </summary>
/// <param name="AuthoredBy">Operator id who authored this save.</param>
/// <param name="AuthoredAtUtc">UTC timestamp of the save.</param>
/// <param name="ParentCommit">The commit this save was based on; <c>null</c> for the first save.</param>
/// <param name="AppVersion">The authoring app version string (e.g. <c>"rw2 …"</c>).</param>
public sealed record VersionInfo(
    string AuthoredBy,
    DateTimeOffset AuthoredAtUtc,
    string? ParentCommit,
    string AppVersion);
