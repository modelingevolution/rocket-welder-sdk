using System.Reflection;
using System.Reflection.Emit;
using FluentAssertions;
using RocketWelder.SDK.Automation.Plugins;

namespace RocketWelder.SDK.Automation.Tests.Plugins;

/// <summary>
/// Tests for <see cref="PluginBootstrapper.PreloadSharedMaxima"/> and its
/// internal folder-scanning logic (<see cref="PluginBootstrapper.ScanFolder"/>).
///
/// Each test that writes fake DLLs to temp dirs calls <see cref="FakeDll.Create"/>
/// which uses <c>PersistedAssemblyBuilder</c> (.NET 9+) to emit a minimal managed
/// assembly with a controlled version number.
/// </summary>
public sealed class PluginBootstrapperTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(), "PluginBootstrapperTests_" + Guid.NewGuid().ToString("N"));

    public PluginBootstrapperTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best-effort cleanup */ }
    }

    // ── ScanFolder — unit tests on the internal scanning logic ────────────────────

    [Fact]
    public void ScanFolder_Finds_SharedAssembly_In_Folder()
    {
        var dir = MkDir("host");
        FakeDll.Create(dir, "MySharedLib", new Version(1, 0, 0, 0));

        var (maxVersions, maxPaths, maxFromPlugin, pluginMajors, sharedNames) = MakeDicts("MySharedLib");

        PluginBootstrapper.ScanFolder(dir, sharedNames, maxVersions, maxPaths, maxFromPlugin,
            pluginMajors, isPluginFolder: false, logger: null);

        maxVersions.Should().ContainKey("MySharedLib");
        maxVersions["MySharedLib"].Should().Be(new Version(1, 0, 0, 0));
        maxPaths["MySharedLib"].Should().EndWith("MySharedLib.dll");
        maxFromPlugin["MySharedLib"].Should().BeFalse("scanned as host folder");
    }

    [Fact]
    public void ScanFolder_Ignores_NonShared_Assembly()
    {
        var dir = MkDir("host");
        FakeDll.Create(dir, "PrivateDep", new Version(3, 0, 0, 0));

        var (maxVersions, _, _, _, sharedNames) = MakeDicts("MySharedLib"); // PrivateDep NOT listed

        PluginBootstrapper.ScanFolder(dir, sharedNames, maxVersions,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, List<(int Major, string FolderPath)>>(StringComparer.OrdinalIgnoreCase),
            isPluginFolder: false, logger: null);

        maxVersions.Should().BeEmpty("PrivateDep is not a shared assembly");
    }

    [Fact]
    public void ScanFolder_HigherPluginVersion_Wins_Over_LowerHostVersion()
    {
        var hostDir   = MkDir("host");
        var pluginDir = MkDir("plugin");
        FakeDll.Create(hostDir,   "MySharedLib", new Version(1, 0, 0, 0));
        FakeDll.Create(pluginDir, "MySharedLib", new Version(1, 1, 0, 0));

        var (maxVersions, maxPaths, maxFromPlugin, pluginMajors, sharedNames) = MakeDicts("MySharedLib");

        // Scan host first (as the bootstrapper does).
        PluginBootstrapper.ScanFolder(hostDir,   sharedNames, maxVersions, maxPaths, maxFromPlugin, pluginMajors, isPluginFolder: false, logger: null);
        PluginBootstrapper.ScanFolder(pluginDir, sharedNames, maxVersions, maxPaths, maxFromPlugin, pluginMajors, isPluginFolder: true,  logger: null);

        maxVersions["MySharedLib"].Should().Be(new Version(1, 1, 0, 0), "plugin's newer minor wins");
        maxPaths["MySharedLib"].Should().Contain("plugin",    "source should be the plugin folder");
        maxFromPlugin["MySharedLib"].Should().BeTrue("max came from plugin folder");
    }

    [Fact]
    public void ScanFolder_LowerPluginVersion_DoesNot_Win_Over_HigherHostVersion()
    {
        var hostDir   = MkDir("host");
        var pluginDir = MkDir("plugin");
        FakeDll.Create(hostDir,   "MySharedLib", new Version(1, 5, 0, 0));
        FakeDll.Create(pluginDir, "MySharedLib", new Version(1, 2, 0, 0));

        var (maxVersions, maxPaths, maxFromPlugin, pluginMajors, sharedNames) = MakeDicts("MySharedLib");

        PluginBootstrapper.ScanFolder(hostDir,   sharedNames, maxVersions, maxPaths, maxFromPlugin, pluginMajors, isPluginFolder: false, logger: null);
        PluginBootstrapper.ScanFolder(pluginDir, sharedNames, maxVersions, maxPaths, maxFromPlugin, pluginMajors, isPluginFolder: true,  logger: null);

        maxVersions["MySharedLib"].Should().Be(new Version(1, 5, 0, 0), "host's higher version wins");
        maxPaths["MySharedLib"].Should().Contain("host");
        maxFromPlugin["MySharedLib"].Should().BeFalse("max came from host folder");
    }

    [Fact]
    public void ScanFolder_SameName_Different_Major_Across_TwoPlugins_RecordsBothMajors()
    {
        var pluginA = MkDir("pluginA");
        var pluginB = MkDir("pluginB");
        FakeDll.Create(pluginA, "MySharedLib", new Version(1, 0, 0, 0));
        FakeDll.Create(pluginB, "MySharedLib", new Version(2, 0, 0, 0));

        var (maxVersions, maxPaths, maxFromPlugin, pluginMajors, sharedNames) = MakeDicts("MySharedLib");

        PluginBootstrapper.ScanFolder(pluginA, sharedNames, maxVersions, maxPaths, maxFromPlugin, pluginMajors, isPluginFolder: true, logger: null);
        PluginBootstrapper.ScanFolder(pluginB, sharedNames, maxVersions, maxPaths, maxFromPlugin, pluginMajors, isPluginFolder: true, logger: null);

        pluginMajors.Should().ContainKey("MySharedLib");
        var majors = pluginMajors["MySharedLib"].Select(m => m.Major).ToList();
        majors.Should().Contain(1).And.Contain(2, "both plugin contributions are tracked");
    }

    // ── PreloadSharedMaxima — cross-plugin incompatibility detection ──────────────

    [Fact]
    public void PreloadSharedMaxima_DetectsCrossPluginIncompatibility()
    {
        var hostDir   = MkDir("host");
        var pluginA   = MkDir("pluginA");
        var pluginB   = MkDir("pluginB");
        // Two plugins carry the same shared lib at incompatible major versions.
        FakeDll.Create(pluginA, "BattleLib", new Version(1, 0, 0, 0));
        FakeDll.Create(pluginB, "BattleLib", new Version(2, 0, 0, 0));

        var sharedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BattleLib" };

        var result = PluginBootstrapper.PreloadSharedMaxima(
            hostDir, [pluginA, pluginB], sharedNames);

        result.CrossPluginIncompatibilities.Should().NotBeEmpty(
            "two plugins need incompatible major versions of BattleLib");
        result.CrossPluginIncompatibilities[0].Should().Contain("BattleLib");
    }

    [Fact]
    public void PreloadSharedMaxima_NoIncompatibility_WhenSameMajorAcrossPlugins()
    {
        var hostDir = MkDir("host");
        var pluginA = MkDir("pluginA");
        var pluginB = MkDir("pluginB");
        FakeDll.Create(pluginA, "BattleLib", new Version(1, 0, 0, 0));
        FakeDll.Create(pluginB, "BattleLib", new Version(1, 2, 0, 0));

        var sharedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BattleLib" };

        var result = PluginBootstrapper.PreloadSharedMaxima(
            hostDir, [pluginA, pluginB], sharedNames);

        result.CrossPluginIncompatibilities.Should().BeEmpty(
            "both plugins use major 1 — compatible");
    }

    [Fact]
    public void PreloadSharedMaxima_ReturnsCorrectMaxVersions()
    {
        var hostDir   = MkDir("host");
        var pluginDir = MkDir("plugin");
        FakeDll.Create(hostDir,   "AlphaLib", new Version(2, 0, 0, 0));
        FakeDll.Create(pluginDir, "AlphaLib", new Version(2, 1, 0, 0));

        var sharedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AlphaLib" };

        var result = PluginBootstrapper.PreloadSharedMaxima(
            hostDir, [pluginDir], sharedNames);

        result.MaxVersions.Should().ContainKey("AlphaLib");
        result.MaxVersions["AlphaLib"].Should().Be(new Version(2, 1, 0, 0));
        result.MaxSourcePaths["AlphaLib"].Should().Contain("plugin");
    }

    [Fact]
    public void PreloadSharedMaxima_NonExistentPluginFolder_DoesNotThrow()
    {
        var hostDir = MkDir("host");
        var sharedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SomeLib" };

        var act = () => PluginBootstrapper.PreloadSharedMaxima(
            hostDir, ["/non/existent/path"], sharedNames);

        act.Should().NotThrow();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private string MkDir(string name)
    {
        var dir = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static (
        Dictionary<string, Version> maxVersions,
        Dictionary<string, string> maxPaths,
        Dictionary<string, bool> maxFromPlugin,
        Dictionary<string, List<(int Major, string FolderPath)>> pluginMajors,
        HashSet<string> sharedNames
    ) MakeDicts(params string[] sharedAssemblyNames)
    {
        return (
            new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, List<(int Major, string FolderPath)>>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(sharedAssemblyNames, StringComparer.OrdinalIgnoreCase)
        );
    }
}

/// <summary>
/// Helper that emits a minimal managed DLL with a known assembly name and version using
/// <c>PersistedAssemblyBuilder</c> (.NET 9 / net10+). Used by tests that need fake shared libs
/// in temp directories without depending on pre-built test fixtures.
/// </summary>
internal static class FakeDll
{
    /// <summary>
    /// Creates <c>&lt;dir&gt;/&lt;assemblyName&gt;.dll</c> with the given version.
    /// The DLL contains a single empty public class so it is a valid managed assembly
    /// that <c>AssemblyName.GetAssemblyName()</c> and <c>Assembly.LoadFrom()</c> can read.
    /// </summary>
    public static string Create(string dir, string assemblyName, Version version)
    {
        var asmName = new AssemblyName(assemblyName) { Version = version };
        // PersistedAssemblyBuilder (.NET 9+): saves IL to disk without running it.
        var ab = new PersistedAssemblyBuilder(asmName, typeof(object).Assembly);

        // Emit a minimal module + type so the IL is valid.
        var mb = ab.DefineDynamicModule(assemblyName);
        mb.DefineType("FakeClass", TypeAttributes.Public | TypeAttributes.Class).CreateType();

        var outputPath = Path.Combine(dir, assemblyName + ".dll");
        ab.Save(outputPath);
        return outputPath;
    }
}
