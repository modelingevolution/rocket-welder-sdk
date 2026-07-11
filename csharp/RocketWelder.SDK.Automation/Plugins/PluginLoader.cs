using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;

namespace RocketWelder.SDK.Automation.Plugins;

/// <summary>
/// Discovers and loads <see cref="IPlugin"/> implementations.
///
/// <para>
/// Plugins load into <see cref="AssemblyLoadContext.Default"/> (mandatory for Blazor/MudBlazor
/// <c>DynamicComponent</c> type unification). A <see cref="AssemblyLoadContext.Default"/> Resolving
/// handler is installed that:
/// <list type="number">
///   <item>
///     For <b>shared assemblies</b> (names in <c>sharedAssemblyNames</c>): returns the
///     already-loaded maximum copy — which the §6.8 bootstrapper (<see cref="PluginBootstrapper"/>)
///     pre-loaded before any host code ran. Everyone (host + plugins) unifies <b>UP</b> to the
///     highest version.
///   </item>
///   <item>
///     For <b>private transitive deps</b>: probes each registered plugin folder so each
///     plugin's private deps (e.g., FluentModbus, vendor CAN libs) resolve from the
///     plugin's own version folder.
///   </item>
/// </list>
/// See device-extensibility.md §6.8 for the full rationale and §6.4 for the Load/Discover
/// entry points.
/// </para>
///
/// <para>
/// <b>2.9.0 backward-compat:</b> the existing <see cref="Discover"/> entry point is unchanged.
/// The new <see cref="Load(IReadOnlyList{string}, IReadOnlySet{string})"/> overload is the
/// iteration-2 addition used by the rw2 PluginService (it receives explicit DLL paths and the
/// shared-assembly name set resolved by rw2's <c>ISharedExclusionOracle</c>).
/// </para>
/// </summary>
public sealed class PluginLoader
{
    // ── Instance state ────────────────────────────────────────────────────────────

    private readonly string? _pluginsDirectory;
    private readonly ILogger<PluginLoader> _logger;

    // ── Static (process-wide) state for the singleton Resolving handler ───────────
    // The ALC.Default.Resolving event is global; we install exactly one handler per
    // process (guard: _resolvingHandlerInstalled). Static fields ensure that calling
    // Load() from one PluginLoader instance and Discover() from another share the
    // same probe paths and shared-name set.

    private static readonly object _handlerLock = new();
    private static volatile bool _resolvingHandlerInstalled;

    // Probe paths registered for each loaded plugin (its version folder).
    // Thread-safety: written on the caller thread (LoadPluginAssembly); read by the
    // Resolving handler on arbitrary ALC threads.  The handler snapshots the list
    // (ToArray) before iterating — safe against concurrent Add during a multi-DLL pass.
    private static readonly List<string> _probePaths = [];

    // The shared-assembly name set supplied by the host (rw2's ISharedExclusionOracle).
    // Null if Discover() is used without Load() (2.9.0 fallback behaviour).
    private static IReadOnlySet<string>? _sharedAssemblyNames;

    // ── Constructors ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a PluginLoader for the flat-directory <see cref="Discover"/> entry point
    /// (2.9.0 API; backward-compatible).
    /// </summary>
    public PluginLoader(string pluginsDirectory, ILogger<PluginLoader> logger)
    {
        _pluginsDirectory = pluginsDirectory;
        _logger = logger;
    }

    // ── Iteration-2 entry point ───────────────────────────────────────────────────

    /// <summary>
    /// Loads the explicit set of plugin DLL paths that the host (rw2 PluginService) has
    /// resolved from the active manifest. This is the iteration-2 entry point; it supersedes
    /// the per-version-folder stopgap used in iteration-1.
    ///
    /// <para>
    /// Prerequisites:
    /// <list type="bullet">
    ///   <item>
    ///     <see cref="PluginBootstrapper.PreloadSharedMaxima"/> MUST have been called at the
    ///     very top of <c>Program.cs</c> — before <c>WebApplication.CreateBuilder</c> —
    ///     so that the highest version of each shared assembly is already in the default ALC
    ///     before any host code JITs a shared binding.
    ///   </item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// For each path:
    /// <list type="number">
    ///   <item>Registers the DLL's directory as a private-dep probe path.</item>
    ///   <item>Calls <c>Assembly.LoadFrom(dllPath)</c> into <see cref="AssemblyLoadContext.Default"/>.</item>
    ///   <item>Scans for <see cref="RocketWelderPluginAttribute"/>-decorated <see cref="IPlugin"/> types.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// FR-15 version-alignment check: after scanning all paths, any plugin whose folder
    /// contains a shared assembly at a major version incompatible with the resolved maximum
    /// is <b>rejected</b> with a logged ERROR message (naming the plugin, assembly, required
    /// version, and resolved max). Rejections are also returned in the second element of the
    /// tuple for surfacing on the Plugins management page.
    /// </para>
    /// </summary>
    /// <param name="pluginDllPaths">
    /// Ordered list of absolute paths to the primary plugin DLL for each active plugin
    /// (e.g. <c>plugins/&lt;id&gt;/&lt;version&gt;/&lt;id&gt;.dll</c>).
    /// </param>
    /// <param name="sharedAssemblyNames">
    /// The set of shared-assembly simple names (case-insensitive) that must unify
    /// across host + plugins. Supplied by rw2's <c>ISharedExclusionOracle.SharedAssemblyNames()</c>.
    /// Must match the set used by <see cref="PluginBootstrapper.PreloadSharedMaxima"/>.
    /// </param>
    /// <returns>
    /// A tuple of (loaded plugins, rejection records). Caller passes loaded plugins into
    /// the Phase-1/Phase-2 loop; rejections are surfaced to the Plugins page / startup log.
    /// </returns>
    public (IReadOnlyList<IPlugin> Plugins, IReadOnlyList<PluginRejection> Rejections) Load(
        IReadOnlyList<string> pluginDllPaths,
        IReadOnlySet<string> sharedAssemblyNames)
    {
        EnsureResolvingHandlerInstalled(sharedAssemblyNames);

        // ── Build the resolved-max map from what the bootstrapper already loaded ──
        // Assembly.GetAssemblies() contains every assembly the bootstrapper pre-loaded
        // plus any the runtime has loaded so far from the host dir.  We only care about
        // names in sharedAssemblyNames.
        var resolvedMax = BuildResolvedMaxFromLoadedAssemblies(sharedAssemblyNames);

        // ── First pass: FR-15 alignment check per plugin folder ───────────────────
        // We check BEFORE loading so a rejected plugin never runs its static ctor.
        var rejectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rejections    = new List<PluginRejection>();

        foreach (var dllPath in pluginDllPaths)
        {
            var folder = Path.GetDirectoryName(dllPath);
            if (folder is null) continue;

            var folderRejections = CheckVersionAlignment(dllPath, folder, sharedAssemblyNames, resolvedMax);
            if (folderRejections.Count > 0)
            {
                foreach (var r in folderRejections)
                {
                    _logger.LogError(
                        "FR-15 rejection — plugin '{PluginPath}': {Message}",
                        dllPath, r.Message);
                }
                rejections.AddRange(folderRejections);
                rejectedPaths.Add(dllPath);
            }
        }

        // ── Second pass: load non-rejected plugins ────────────────────────────────
        var plugins = new List<IPlugin>();

        foreach (var dllPath in pluginDllPaths)
        {
            if (rejectedPaths.Contains(dllPath))
            {
                _logger.LogWarning("Skipping rejected plugin: '{Path}'.", dllPath);
                continue;
            }

            var folder = Path.GetDirectoryName(dllPath) ?? string.Empty;
            LoadPluginAssembly(dllPath, folder, plugins);
        }

        _logger.LogInformation(
            "Plugin load complete: {Loaded} plugin(s) loaded, {Rejected} rejected.",
            plugins.Count, rejectedPaths.Count);

        return (plugins, rejections);
    }

    // ── 2.9.0 entry point (unchanged) ────────────────────────────────────────────

    /// <summary>
    /// Discovers all plugins from the configured directory (2.9.0 API; backward-compatible).
    /// Each immediate subdirectory (or any <c>*.dll</c> in the root) is treated as a
    /// potential plugin assembly.
    ///
    /// <para>
    /// For the highest-version-wins behaviour (iteration-2), use
    /// <see cref="Load(IReadOnlyList{string}, IReadOnlySet{string})"/> together with
    /// <see cref="PluginBootstrapper.PreloadSharedMaxima"/> instead.
    /// </para>
    /// </summary>
    public IReadOnlyList<IPlugin> Discover()
    {
        if (!Directory.Exists(_pluginsDirectory))
        {
            _logger.LogInformation("Plugin directory '{Dir}' does not exist — no plugins loaded.", _pluginsDirectory);
            return [];
        }

        EnsureResolvingHandlerInstalled(sharedAssemblyNames: null);

        var plugins = new List<IPlugin>();

        // Scan immediate subdirectories (each is a plugin's staged publish closure)
        foreach (var pluginDir in Directory.GetDirectories(_pluginsDirectory!))
        {
            var dirName = Path.GetFileName(pluginDir);
            var dllPath = Path.Combine(pluginDir, dirName + ".dll");
            if (!File.Exists(dllPath))
            {
                // Try any single DLL in the directory named differently
                var dlls = Directory.GetFiles(pluginDir, "*.dll");
                if (dlls.Length == 1)
                    dllPath = dlls[0];
                else
                {
                    _logger.LogWarning("No plugin DLL found in '{Dir}' (expected '{Expected}.dll').", pluginDir, dirName);
                    continue;
                }
            }

            LoadPluginAssembly(dllPath, pluginDir, plugins);
        }

        // Also scan DLLs directly in the root (flat deployment option)
        foreach (var dllPath in Directory.GetFiles(_pluginsDirectory!, "*.dll"))
        {
            LoadPluginAssembly(dllPath, _pluginsDirectory!, plugins);
        }

        _logger.LogInformation("Plugin discovery complete: {Count} plugin(s) loaded.", plugins.Count);
        return plugins;
    }

    // ── Shared loading helper ─────────────────────────────────────────────────────

    private void LoadPluginAssembly(string dllPath, string probeDirectory, List<IPlugin> plugins)
    {
        // Register probe path BEFORE loading so the Resolving handler can find private deps.
        lock (_handlerLock)
            _probePaths.Add(probeDirectory);

        Assembly asm;
        try
        {
            asm = Assembly.LoadFrom(dllPath);
            _logger.LogDebug("Loaded plugin assembly: {Path}", dllPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load plugin assembly '{Path}'.", dllPath);
            return;
        }

        // MINOR-5: GetExportedTypes() can throw ReflectionTypeLoadException when the assembly
        // references a type from a dependency that failed to load. Catch per-DLL so one bad
        // assembly doesn't abort the entire discovery pass.
        Type[] exportedTypes;
        try
        {
            exportedTypes = asm.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException rtex)
        {
            _logger.LogWarning(rtex,
                "Could not load all exported types from '{Path}' — partial scan ({Loaded}/{Total} types resolved).",
                dllPath,
                rtex.Types.Count(t => t != null),
                rtex.Types.Length);
            exportedTypes = rtex.Types.Where(t => t != null).ToArray()!;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not enumerate exported types in '{Path}' — skipping DLL.", dllPath);
            return;
        }

        foreach (var type in exportedTypes)
        {
            if (type.IsAbstract || type.IsInterface) continue;
            if (!typeof(IPlugin).IsAssignableFrom(type)) continue;
            if (type.GetCustomAttribute<RocketWelderPluginAttribute>() is null) continue;

            try
            {
                var plugin = (IPlugin)Activator.CreateInstance(type)!;
                plugins.Add(plugin);
                _logger.LogInformation("Discovered plugin: {Name} ({Type})",
                    type.GetCustomAttribute<RocketWelderPluginAttribute>()!.Name, type.FullName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to instantiate plugin type '{Type}'.", type.FullName);
            }
        }
    }

    // ── Default ALC Resolving handler (singleton, installed once per process) ─────

    private static void EnsureResolvingHandlerInstalled(IReadOnlySet<string>? sharedAssemblyNames)
    {
        lock (_handlerLock)
        {
            // Merge in any newly supplied shared names.
            if (sharedAssemblyNames is { Count: > 0 })
            {
                if (_sharedAssemblyNames is null)
                {
                    _sharedAssemblyNames = sharedAssemblyNames;
                }
                else if (!ReferenceEquals(_sharedAssemblyNames, sharedAssemblyNames))
                {
                    // Merge: union of both sets. Defensive against multiple Load() calls with
                    // slightly different sets (shouldn't happen in practice, but be safe).
                    _sharedAssemblyNames = _sharedAssemblyNames
                        .Union(sharedAssemblyNames, StringComparer.OrdinalIgnoreCase)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                }
            }

            if (_resolvingHandlerInstalled) return;
            _resolvingHandlerInstalled = true;
        }

        AssemblyLoadContext.Default.Resolving += ResolvingHandler;
    }

    /// <summary>
    /// The singleton Resolving handler installed on <see cref="AssemblyLoadContext.Default"/>.
    /// </summary>
    private static Assembly? ResolvingHandler(AssemblyLoadContext ctx, AssemblyName name)
    {
        // ── 1. Shared assembly → return the resident maximum copy ─────────────────
        // The §6.8 bootstrapper pre-loaded the max of each shared assembly before any
        // host code ran.  Returning the already-loaded copy here ensures every consumer
        // (host + all plugins) unifies UP to that max, not down to whatever they
        // individually referenced.
        //
        // For private deps we intentionally fall through to probing (step 2) so each
        // plugin can have its own private dep version.
        var sharedNames = _sharedAssemblyNames;
        if (sharedNames is not null && name.Name is not null &&
            sharedNames.Contains(name.Name))
        {
            var resident = ctx.Assemblies.FirstOrDefault(a =>
                string.Equals(a.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase));
            if (resident is not null)
                return resident;
            // Shared assembly not yet loaded — fall through to probe (bootstrapper may
            // have missed it if it wasn't in any scanned folder).
        }

        // ── 2. Private dep → probe each registered plugin folder ──────────────────
        // Snapshot _probePaths before iterating — thread-safe against concurrent Add
        // (Resolving fires on arbitrary ALC threads; Add fires on the caller thread).
        string[] snapshot;
        lock (_handlerLock)
            snapshot = [.. _probePaths];

        foreach (var probeDir in snapshot)
        {
            var candidate = Path.Combine(probeDir, name.Name + ".dll");
            if (File.Exists(candidate))
            {
                try { return Assembly.LoadFrom(candidate); }
                catch { /* fall through to next probe dir */ }
            }
        }

        return null;
    }

    // ── FR-15 helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the current loaded-assembly set and returns the highest version of each
    /// shared assembly that is already in the default ALC (set by the bootstrapper or
    /// by the runtime's own probing).
    /// </summary>
    private static Dictionary<string, Version> BuildResolvedMaxFromLoadedAssemblies(
        IReadOnlySet<string> sharedAssemblyNames)
    {
        var result = new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);
        foreach (var asm in AssemblyLoadContext.Default.Assemblies)
        {
            var n = asm.GetName();
            if (n.Name is null || !sharedAssemblyNames.Contains(n.Name)) continue;
            var ver = n.Version;
            if (ver is null) continue;
            if (!result.TryGetValue(n.Name, out var current) || ver > current)
                result[n.Name] = ver;
        }
        return result;
    }

    /// <summary>
    /// FR-15 check: scans <paramref name="pluginFolder"/> for shared-assembly DLLs
    /// and returns a rejection record for each one whose major version is incompatible
    /// with the resolved maximum.
    /// </summary>
    private static List<PluginRejection> CheckVersionAlignment(
        string pluginDllPath,
        string pluginFolder,
        IReadOnlySet<string> sharedAssemblyNames,
        Dictionary<string, Version> resolvedMax)
    {
        var result = new List<PluginRejection>();

        if (!Directory.Exists(pluginFolder))
            return result;

        foreach (var dllPath in Directory.GetFiles(pluginFolder, "*.dll"))
        {
            AssemblyName asmName;
            try
            {
                asmName = AssemblyName.GetAssemblyName(dllPath);
            }
            catch
            {
                continue; // Native DLL or unreadable — skip.
            }

            var simpleName = asmName.Name;
            if (simpleName is null || !sharedAssemblyNames.Contains(simpleName)) continue;

            var pluginVersion = asmName.Version;
            if (pluginVersion is null) continue;

            // Only flag shared assemblies that differ from the resolved max.
            if (!resolvedMax.TryGetValue(simpleName, out var maxVersion))
                continue; // Not yet loaded — no conflict to check.

            // Backward-compatible: same major, any minor/patch can roll forward.
            // Incompatible: different major (plugin needs a major the resolved max doesn't satisfy).
            if (pluginVersion.Major == maxVersion.Major)
                continue; // Compatible — no rejection.

            var msg = $"Plugin requires '{simpleName} {pluginVersion}' (major {pluginVersion.Major}) " +
                      $"but the resolved process-wide maximum is {maxVersion} (major {maxVersion.Major}): " +
                      $"incompatible major versions. " +
                      $"Install a major-{maxVersion.Major}-compatible build of this plugin, " +
                      $"or ensure all active plugins agree on a compatible major version of '{simpleName}'.";

            result.Add(new PluginRejection(
                PluginDllPath: pluginDllPath,
                AssemblyName: simpleName,
                RequiredVersion: pluginVersion,
                ResolvedMaxVersion: maxVersion,
                Message: msg));
        }

        return result;
    }
}
