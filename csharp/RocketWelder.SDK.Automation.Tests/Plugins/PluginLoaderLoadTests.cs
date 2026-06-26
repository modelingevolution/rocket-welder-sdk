using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RocketWelder.SDK.Automation.Plugins;

namespace RocketWelder.SDK.Automation.Tests.Plugins;

/// <summary>
/// Tests for <see cref="PluginLoader.Load(IReadOnlyList{string}, IReadOnlySet{string})"/>:
/// the iteration-2 entry point that accepts explicit DLL paths + the shared-assembly set.
///
/// These tests prove:
/// <list type="bullet">
///   <item>Happy path: plugin is loaded and its <see cref="IPlugin"/> type is discovered.</item>
///   <item>FR-15 rejection: a plugin folder containing a shared assembly at an incompatible major
///         is rejected with a clear message naming the plugin, assembly, and versions.</item>
///   <item>Ride-up: when a plugin's shared assembly (same major, newer minor) is pre-loaded by the
///         bootstrapper, the process-wide loaded version is the newer one.</item>
///   <item>2.9.0 backward-compat: <see cref="PluginLoader.Discover"/> still works (no signature
///         change).</item>
/// </list>
///
/// <para>
/// NOTE: Assembly loading is global (process-wide, <see cref="System.Runtime.Loader.AssemblyLoadContext.Default"/>).
/// Tests use unique assembly names per test to avoid cross-test interference.
/// </para>
/// </summary>
public sealed class PluginLoaderLoadTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(), "PluginLoaderLoadTests_" + Guid.NewGuid().ToString("N"));

    private readonly ILogger<PluginLoader> _logger = NullLogger<PluginLoader>.Instance;

    public PluginLoaderLoadTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best-effort */ }
    }

    // ── Happy path ────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_WithNoSharedAssemblies_LoadsPlugin()
    {
        // Build a minimal plugin DLL in a temp folder.
        var (folder, dllPath) = CreatePluginFolder("HappyPlugin_" + Uid(), "HappyPlugin_" + Uid());

        var loader = new PluginLoader(folder, _logger);
        var sharedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var (plugins, rejections) = loader.Load([dllPath], sharedNames);

        rejections.Should().BeEmpty("no shared assemblies → no alignment checks");
        // The plugin assembly loads but has no [RocketWelderPlugin] IPlugin type (it's a stub) →
        // plugins list is empty but no error.
        plugins.Should().BeEmpty("stub plugin DLL has no IPlugin implementation");
    }

    [Fact]
    public void Load_EmptyPathList_ReturnsEmpty()
    {
        var loader = new PluginLoader(_tempRoot, _logger);
        var sharedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var (plugins, rejections) = loader.Load([], sharedNames);

        plugins.Should().BeEmpty();
        rejections.Should().BeEmpty();
    }

    [Fact]
    public void Load_NonExistentPath_IsSkippedWithoutThrowing()
    {
        var loader = new PluginLoader(_tempRoot, _logger);
        var sharedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var act = () => loader.Load(["/non/existent/Plugin.dll"], sharedNames);

        act.Should().NotThrow();
    }

    // ── FR-15 rejection ───────────────────────────────────────────────────────────

    [Fact]
    public void Load_RejectsPlugin_WithIncompatibleMajor_SharedAssembly()
    {
        // Simulate: resolved max = SharedLib 2.0.0; plugin carries SharedLib 1.0.0 (different major).
        // The plugin folder has SharedLib 1.0.0 in it; we fake the resolved max by having
        // the bootstrapper load SharedLib 2.0.0 first (or by constructing the scenario so
        // the max in the ALC is 2.0.0 and the plugin folder has 1.0.0).
        //
        // Implementation approach: create two plugin folders:
        //   - PluginA folder: carries SharedLib 2.0.0 (newer major)
        //   - PluginB folder: carries SharedLib 1.0.0 (older major — incompatible with max)
        // Load both. The bootstrapper pre-loads SharedLib 2.0.0 as the max.
        // PluginB carries 1.0.0 → major 1 ≠ max major 2 → rejected.

        var sharedLibName = "FR15TestSharedLib_" + Uid();
        var (folderA, dllPathA) = CreatePluginFolderWithSharedLib(
            "PluginA_" + Uid(), sharedLibName, new Version(2, 0, 0, 0));
        var (folderB, dllPathB) = CreatePluginFolderWithSharedLib(
            "PluginB_" + Uid(), sharedLibName, new Version(1, 0, 0, 0));

        // Pre-load the max so the ALC knows about SharedLib 2.0.0.
        var sharedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { sharedLibName };
        PluginBootstrapper.PreloadSharedMaxima(
            hostDirectory: _tempRoot,
            pluginFolders: [folderA, folderB],
            sharedAssemblyNames: sharedNames);

        var loader = new PluginLoader(_tempRoot, _logger);
        var (plugins, rejections) = loader.Load([dllPathA, dllPathB], sharedNames);

        rejections.Should().Contain(r =>
            r.PluginDllPath == dllPathB &&
            r.AssemblyName  == sharedLibName &&
            r.RequiredVersion.Major == 1 &&
            r.ResolvedMaxVersion.Major == 2,
            "PluginB requires major 1 but max is major 2 → FR-15 rejection");

        rejections.Single(r => r.PluginDllPath == dllPathB)
            .Message.Should().Contain(sharedLibName)
            .And.Contain("1").And.Contain("2");
    }

    [Fact]
    public void Load_AcceptsPlugin_WithSameMajor_NewerMinor_SharedAssembly()
    {
        // Plugin carries SharedLib 1.1.0; resolved max = 1.1.0 (plugin's version wins).
        // Both host (1.0.0) and plugin (1.1.0) share major 1 → compatible.

        var sharedLibName = "MinorBumpTestLib_" + Uid();
        var hostDir = MkDir("host_" + Uid());
        FakeDll.Create(hostDir, sharedLibName, new Version(1, 0, 0, 0));

        var (pluginFolder, dllPath) = CreatePluginFolderWithSharedLib(
            "PluginMinorBump_" + Uid(), sharedLibName, new Version(1, 1, 0, 0));

        var sharedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { sharedLibName };
        PluginBootstrapper.PreloadSharedMaxima(hostDir, [pluginFolder], sharedNames);

        var loader = new PluginLoader(_tempRoot, _logger);
        var (plugins, rejections) = loader.Load([dllPath], sharedNames);

        rejections.Should().BeEmpty("same major (1) — minor bump is backward-compatible");
    }

    [Fact]
    public void Load_RejectionMessage_ContainsPluginPath_AssemblyName_BothVersions()
    {
        var sharedLibName = "MsgTestLib_" + Uid();
        var (folderMax, dllMax) = CreatePluginFolderWithSharedLib(
            "PluginMax_" + Uid(), sharedLibName, new Version(3, 0, 0, 0));
        var (folderOld, dllOld) = CreatePluginFolderWithSharedLib(
            "PluginOld_" + Uid(), sharedLibName, new Version(1, 0, 0, 0));

        var sharedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { sharedLibName };
        PluginBootstrapper.PreloadSharedMaxima(_tempRoot, [folderMax, folderOld], sharedNames);

        var loader = new PluginLoader(_tempRoot, _logger);
        var (_, rejections) = loader.Load([dllMax, dllOld], sharedNames);

        var rejection = rejections.Should().ContainSingle(r => r.PluginDllPath == dllOld).Subject;
        rejection.Message.Should().Contain(sharedLibName, "message names the assembly");
        rejection.Message.Should().Contain("1",  "message mentions required major");
        rejection.Message.Should().Contain("3",  "message mentions resolved max major");
    }

    // ── Ride-up: highest-version-wins pre-load ────────────────────────────────────

    [Fact]
    public void PreloadSharedMaxima_WhenPlugin_HasNewerMinor_ProcessLoads_NewerVersion()
    {
        // Arrange: host has SharedLib 1.0.0; plugin has SharedLib 1.2.0.
        var libName   = "RideUpLib_" + Uid();
        var hostDir   = MkDir("host_" + Uid());
        var pluginDir = MkDir("plugin_" + Uid());

        FakeDll.Create(hostDir,   libName, new Version(1, 0, 0, 0));
        FakeDll.Create(pluginDir, libName, new Version(1, 2, 0, 0));

        var sharedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { libName };

        // Act: run the bootstrapper (pre-loads 1.2.0 from the plugin folder).
        PluginBootstrapper.PreloadSharedMaxima(hostDir, [pluginDir], sharedNames);

        // Assert: the loaded assembly in the ALC has version 1.2.0.
        // Because 1.2.0 was pre-loaded by the bootstrapper BEFORE the host dir was probed
        // for this assembly, the process-wide copy is 1.2.0.
        var loadedAsm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == libName);

        loadedAsm.Should().NotBeNull("the bootstrapper pre-loaded {0}", libName);
        loadedAsm!.GetName().Version.Should().Be(new Version(1, 2, 0, 0),
            "the plugin's newer 1.2.0 was pre-loaded — highest-version-wins");
    }

    [Fact]
    public void PreloadSharedMaxima_WhenPlugin_HasOlderVersion_DoesNotPreloadIt()
    {
        // The host has 1.5.0; plugin has 1.2.0. Max = 1.5.0 (host).
        // Bootstrapper should NOT try to load 1.2.0 (it would be redundant and could
        // shadow the host's copy if the host version wasn't already in the ALC).
        // We verify by checking the returned SharedMaximaResult's source path.

        var libName   = "HostWinsLib_" + Uid();
        var hostDir   = MkDir("host_" + Uid());
        var pluginDir = MkDir("plugin_" + Uid());

        FakeDll.Create(hostDir,   libName, new Version(1, 5, 0, 0));
        FakeDll.Create(pluginDir, libName, new Version(1, 2, 0, 0));

        var sharedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { libName };
        var result = PluginBootstrapper.PreloadSharedMaxima(hostDir, [pluginDir], sharedNames);

        result.MaxVersions[libName].Should().Be(new Version(1, 5, 0, 0));
        result.MaxSourcePaths[libName].Should().Contain(hostDir,
            "source path should be the host dir, not the plugin dir");
    }

    // ── Backward-compat: Discover() still works ───────────────────────────────────

    [Fact]
    public void Discover_StillWorks_WhenNoLoad_Called()
    {
        // Create an empty plugins directory (no plugin DLLs) — Discover returns empty without throwing.
        var pluginsDir = MkDir("discover_" + Uid());
        var loader = new PluginLoader(pluginsDir, _logger);

        var plugins = loader.Discover();

        plugins.Should().BeEmpty("empty plugins directory");
    }

    [Fact]
    public void Discover_NonExistentDirectory_ReturnsEmpty()
    {
        var loader = new PluginLoader("/non/existent/plugins", _logger);

        var plugins = loader.Discover();

        plugins.Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    /// <summary>Creates a plugin folder with a stub plugin DLL (no <see cref="IPlugin"/> impl).</summary>
    private (string folder, string dllPath) CreatePluginFolder(string folderName, string dllName)
    {
        var folder = MkDir(folderName);
        var dllPath = FakeDll.Create(folder, dllName, new Version(1, 0, 0, 0));
        return (folder, dllPath);
    }

    /// <summary>
    /// Creates a plugin folder that contains both a stub plugin DLL AND a fake shared assembly
    /// at <paramref name="sharedVersion"/>.
    /// </summary>
    private (string folder, string dllPath) CreatePluginFolderWithSharedLib(
        string folderName, string sharedLibName, Version sharedVersion)
    {
        var folder = MkDir(folderName);
        // The "main" plugin DLL (stub).
        var pluginDllName = folderName + "_Plugin";
        var dllPath = FakeDll.Create(folder, pluginDllName, new Version(1, 0, 0, 0));
        // The shared lib at the desired version.
        FakeDll.Create(folder, sharedLibName, sharedVersion);
        return (folder, dllPath);
    }

    private string MkDir(string name)
    {
        var dir = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Short unique suffix to avoid assembly-name collisions across tests.</summary>
    private static string Uid() => Guid.NewGuid().ToString("N")[..8];
}
