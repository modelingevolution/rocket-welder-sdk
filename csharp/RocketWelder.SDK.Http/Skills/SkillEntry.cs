namespace RocketWelder.SDK.Http.Skills;

/// <summary>
/// Index entry for a skill. The full markdown body is fetched separately via
/// <see cref="ISkillsApi.LoadAsync"/> so the index list stays small even when
/// skills are long. Parsed from the YAML frontmatter the skill file ships with.
/// </summary>
/// <param name="Name">Skill name as published. Matches the filename stem.</param>
/// <param name="Description">One-line summary from the frontmatter <c>description</c> field.</param>
/// <param name="Triggers">Optional trigger phrases the model can use to decide when to load.</param>
public sealed record SkillEntry(
    string Name,
    string Description,
    IReadOnlyList<string> Triggers);
