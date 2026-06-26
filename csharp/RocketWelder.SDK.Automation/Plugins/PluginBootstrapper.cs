using System.Reflection;
using Microsoft.Extensions.Logging;

namespace RocketWelder.SDK.Automation.Plugins;

/// <summary>
/// §6.8 bootstrapper: pre-loads the highest version of each shared assembly across
/// {host directory ∪ all active plugin folders} <b>before any host code JITs a
/// shared-assembly binding</b>.
///
/// <para>
/// Call <see cref="PreloadSharedMaxima"/> at the <b>very top of Program.cs, BEFORE
/// <c>WebApplication.CreateBuilder(...)</c></b>. This ensures that when the host's
/// IL references e.g. <c>ModelingEvolution.Drawing 1.9</c>, the ALC already has
/// the higher plugin-sourced copy loaded and .NET uses it — everyone rides UP.
/// </para>
///
/// <para>
/// After this call, install the <see cref="PluginLoader"/> resolving handler (done
/// inside <see cref="PluginLoader.Load"/>) so that shared-assembly requests return
/// the pre-loaded maximum and private deps are probed from the plugin's version folder.
/// </para>
/// </summary>
public static class PluginBootstrapper
{
    /// <summary>
    /// Scans <paramref name="hostDirectory"/> and every folder in
    /// <paramref name="pluginFolders"/> for DLLs whose simple name appears in
    /// <paramref name="sharedAssemblyNames"/>. Determines the maximum version of
    /// each, then pre-loads any maximum that lives in a plugin folder (so the
    /// process-wide winner is already in <see cref="System.Runtime.Loader.AssemblyLoadContext.Default"/>
    /// before host code touches it).
    /// </summary>
    /// <param name="hostDirectory">
    /// The host application's base directory (<c>AppContext.BaseDirectory</c> in rw2).
    /// DLLs here are auto-probed by the runtime and do not need to be pre-loaded,
    /// but their versions ARE included in the maximum computation.
    /// </param>
    /// <param name="pluginFolders">
    /// Active plugin version folders (e.g. <c>plugins/&lt;id&gt;/&lt;version&gt;/</c>).
    /// If a plugin carries a deliberately-newer shared lib (FR-2/BR-1) it must be
    /// in this folder — the bootstrapper will pre-load it.
    /// </param>
    /// <param name="sharedAssemblyNames">
    /// The set of shared-assembly simple names to unify (same set passed to
    /// <see cref="PluginLoader.Load"/>). Only DLLs whose simple name is in this
    /// set are considered for pre-loading.
    /// </param>
    /// <param name="logger">Optional logger for diagnostic messages.</param>
    /// <returns>
    /// A <see cref="SharedMaximaResult"/> describing the resolved maxima, their
    /// source paths, and any cross-plugin major incompatibilities (FR-15 case b).
    /// </returns>
    public static SharedMaximaResult PreloadSharedMaxima(
        string hostDirectory,
        IEnumerable<string> pluginFolders,
        IReadOnlySet<string> sharedAssemblyNames,
        ILogger? logger = null)
    {
        // ── 1. Scan all folders → collect (name → max version, source path, isFromPlugin) ──

        // For each shared assembly name we track the best (version, path) seen so far,
        // AND whether that best came from a plugin folder (isFromPluginFolder=true) or
        // the host dir (false). We must track this explicitly because plugin folders are
        // typically subdirectories of the app/host directory; a directory-containment check
        // would incorrectly classify plugin-sourced maxima as "host-provided".
        var maxVersions       = new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);
        var maxSourcePaths    = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var maxFromPlugin     = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // Per-plugin-folder major contributions for cross-plugin conflict detection (FR-15 case b).
        // Key = assembly name; Value = list of (major, folderPath) from plugin folders.
        var pluginMajors = new Dictionary<string, List<(int Major, string FolderPath)>>(
            StringComparer.OrdinalIgnoreCase);

        // Host directory first (isPluginFolder=false).
        ScanFolder(hostDirectory, sharedAssemblyNames, maxVersions, maxSourcePaths, maxFromPlugin,
            pluginMajors, isPluginFolder: false, logger);

        // Plugin folders.
        var pluginFolderList = pluginFolders.ToList();
        foreach (var folder in pluginFolderList)
        {
            ScanFolder(folder, sharedAssemblyNames, maxVersions, maxSourcePaths, maxFromPlugin,
                pluginMajors, isPluginFolder: true, logger);
        }

        // ── 2. Detect cross-plugin major incompatibilities (FR-15 case b) ─────────

        var incompatibilities = new List<string>();
        foreach (var (assemblyName, contributions) in pluginMajors)
        {
            var distinctMajors = contributions.Select(c => c.Major).Distinct().ToList();
            if (distinctMajors.Count > 1)
            {
                var byMajor = contributions
                    .GroupBy(c => c.Major)
                    .Select(g => $"major {g.Key} (from {string.Join(", ", g.Select(c => c.FolderPath))})")
                    .ToList();

                var msg = $"Cross-plugin incompatibility: shared assembly '{assemblyName}' " +
                          $"is required at {string.Join(" AND ", byMajor)} — " +
                          $"mutually incompatible majors; only one can be active. " +
                          $"Resolve by pinning a compatible plugin set.";

                incompatibilities.Add(msg);
                logger?.LogError("{Message}", msg);
            }
        }

        // ── 3. Pre-load maxima that came from plugin folders ──────────────────────
        // Host-dir DLLs are auto-probed by the runtime and must NOT be pre-loaded here
        // (the runtime already handles them via AppContext.BaseDirectory probing).
        // Only pre-load when the max version was found in a PLUGIN folder, so the ALC sees
        // the newer version BEFORE any host code JITs a shared-assembly binding.

        foreach (var (assemblyName, sourcePath) in maxSourcePaths)
        {
            if (!maxFromPlugin.TryGetValue(assemblyName, out var fromPlugin) || !fromPlugin)
            {
                logger?.LogDebug(
                    "Bootstrapper: shared assembly '{Name}' max version {Version} is host-provided; " +
                    "no pre-load needed.",
                    assemblyName, maxVersions[assemblyName]);
                continue;
            }

            // Max version lives in a plugin folder — pre-load it so the ALC has it first.
            try
            {
                var loaded = Assembly.LoadFrom(sourcePath);
                var loadedVer = loaded.GetName().Version;
                logger?.LogInformation(
                    "Bootstrapper: pre-loaded {Name} {Version} from '{Path}' (highest-version-wins).",
                    assemblyName, loadedVer, sourcePath);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex,
                    "Bootstrapper: failed to pre-load '{Path}' — shared assembly '{Name}' " +
                    "may resolve to an older version.",
                    sourcePath, assemblyName);
            }
        }

        return new SharedMaximaResult
        {
            MaxVersions                = maxVersions,
            MaxSourcePaths             = maxSourcePaths,
            CrossPluginIncompatibilities = incompatibilities,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans all *.dll files in <paramref name="folder"/> and updates
    /// <paramref name="maxVersions"/> / <paramref name="maxSourcePaths"/> /
    /// <paramref name="maxFromPlugin"/> when a higher version of a shared assembly is found.
    /// If <paramref name="isPluginFolder"/> is <see langword="true"/>, also records the
    /// major version in <paramref name="pluginMajors"/> for cross-plugin conflict detection.
    /// </summary>
    internal static void ScanFolder(
        string folder,
        IReadOnlySet<string> sharedAssemblyNames,
        Dictionary<string, Version> maxVersions,
        Dictionary<string, string> maxSourcePaths,
        Dictionary<string, bool> maxFromPlugin,
        Dictionary<string, List<(int Major, string FolderPath)>> pluginMajors,
        bool isPluginFolder,
        ILogger? logger)
    {
        if (!Directory.Exists(folder))
        {
            logger?.LogDebug("Bootstrapper: folder '{Folder}' does not exist — skipping scan.", folder);
            return;
        }

        foreach (var dllPath in Directory.GetFiles(folder, "*.dll"))
        {
            AssemblyName asmName;
            try
            {
                asmName = AssemblyName.GetAssemblyName(dllPath);
            }
            catch
            {
                // Not a managed assembly (native DLL, etc.) — skip silently.
                continue;
            }

            var simpleName = asmName.Name;
            if (simpleName is null) continue;
            if (!sharedAssemblyNames.Contains(simpleName)) continue;

            var version = asmName.Version;
            if (version is null) continue;

            // Update max if this version is higher.
            if (!maxVersions.TryGetValue(simpleName, out var currentMax) || version > currentMax)
            {
                maxVersions[simpleName]    = version;
                maxSourcePaths[simpleName] = dllPath;
                maxFromPlugin[simpleName]  = isPluginFolder;
            }

            // Track per-plugin-folder majors for cross-plugin conflict detection.
            if (isPluginFolder)
            {
                if (!pluginMajors.TryGetValue(simpleName, out var majors))
                    pluginMajors[simpleName] = majors = [];
                majors.Add((version.Major, folder));
            }
        }
    }
}
