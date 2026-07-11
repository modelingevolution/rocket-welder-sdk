namespace RocketWelder.SDK.Http.Skills;

/// <summary>
/// <c>/api/skills</c> — discovery and loading of deployment-managed skill
/// markdown files. Skill content lives in the rocketwelder-compose repo and is
/// hot-reloaded on the server when files change on disk.
/// </summary>
public interface ISkillsApi
{
    /// <summary>
    /// <c>GET /api/skills</c> — the skill index parsed from each file's YAML
    /// frontmatter. Empty list when the skills folder is empty or unmounted.
    /// </summary>
    Task<IReadOnlyList<SkillEntry>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// <c>GET /api/skills/{name}</c> — the full markdown body of one skill,
    /// frontmatter stripped. Throws <see cref="HttpRequestException"/> with
    /// 404 when the name is not registered.
    /// </summary>
    Task<string> LoadAsync(string name, CancellationToken ct = default);
}
