using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using RocketWelder.SDK.Automation.Plugins;

namespace RocketWelder.SDK.Automation.Plugins;

/// <summary>
/// Discovers and loads <see cref="IPlugin"/> implementations from a configured plugins directory.
///
/// <para>
/// Plugins load into <see cref="AssemblyLoadContext.Default"/> (mandatory for Blazor/MudBlazor
/// <c>DynamicComponent</c> type unification). A <see cref="AssemblyLoadContext.Default"/> Resolving
/// handler is installed that:
/// <list type="number">
///   <item>Returns the already-loaded host assembly for any shared contract (prevents duplicate loads).</item>
///   <item>Probes the plugin's own folder for private transitive dependencies (e.g., FluentModbus).</item>
/// </list>
/// See device-extensibility.md §6.8 for the full rationale.
/// </para>
/// </summary>
public sealed class PluginLoader
{
    private readonly string _pluginsDirectory;
    private readonly ILogger<PluginLoader> _logger;
    private bool _resolvingHandlerInstalled;

    public PluginLoader(string pluginsDirectory, ILogger<PluginLoader> logger)
    {
        _pluginsDirectory = pluginsDirectory;
        _logger = logger;
    }

    /// <summary>
    /// Discovers all plugins from the configured directory. Each immediate subdirectory (or any
    /// <c>*.dll</c> in the root) is treated as a potential plugin assembly.
    /// Returns instantiated <see cref="IPlugin"/> objects, ready for Phase 1 and Phase 2.
    /// </summary>
    public IReadOnlyList<IPlugin> Discover()
    {
        if (!Directory.Exists(_pluginsDirectory))
        {
            _logger.LogInformation("Plugin directory '{Dir}' does not exist — no plugins loaded.", _pluginsDirectory);
            return [];
        }

        EnsureResolvingHandlerInstalled();

        var plugins = new List<IPlugin>();

        // Scan immediate subdirectories (each is a plugin's staged publish closure)
        foreach (var pluginDir in Directory.GetDirectories(_pluginsDirectory))
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
        foreach (var dllPath in Directory.GetFiles(_pluginsDirectory, "*.dll"))
        {
            LoadPluginAssembly(dllPath, _pluginsDirectory, plugins);
        }

        _logger.LogInformation("Plugin discovery complete: {Count} plugin(s) loaded.", plugins.Count);
        return plugins;
    }

    private void LoadPluginAssembly(string dllPath, string probeDirectory, List<IPlugin> plugins)
    {
        // Register probe path BEFORE loading so the Resolving handler can find private deps
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

        foreach (var type in asm.GetExportedTypes())
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

    // ── Default ALC Resolving handler ─────────────────────────────────────────

    // Probe paths added as each plugin assembly is loaded
    private readonly List<string> _probePaths = [];

    private void EnsureResolvingHandlerInstalled()
    {
        if (_resolvingHandlerInstalled) return;
        _resolvingHandlerInstalled = true;

        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            // 1. Unify shared contracts — if the host already has it loaded, return that instance.
            var already = ctx.Assemblies.FirstOrDefault(a =>
                string.Equals(a.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase));
            if (already != null)
                return already;

            // 2. Probe plugin directories for private transitive deps.
            foreach (var probeDir in _probePaths)
            {
                var candidate = Path.Combine(probeDir, name.Name + ".dll");
                if (File.Exists(candidate))
                {
                    try { return Assembly.LoadFrom(candidate); }
                    catch { /* fall through */ }
                }
            }

            return null;
        };

        _logger.LogDebug("Default ALC Resolving handler installed for plugin dependency probing.");
    }
}
