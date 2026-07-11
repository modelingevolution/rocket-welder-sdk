namespace RocketWelder.SDK.Automation.Plugins;

/// <summary>
/// Result from <see cref="PluginBootstrapper.PreloadSharedMaxima"/>: the resolved
/// maximum version of each shared assembly across {host ∪ all active plugin folders},
/// together with which source path carries that maximum and any cross-plugin major
/// incompatibilities detected during the scan.
/// </summary>
public sealed class SharedMaximaResult
{
    /// <summary>
    /// Resolved max version of each shared assembly, keyed by simple assembly name
    /// (case-insensitive). Only names that were found in at least one scanned folder
    /// appear here.
    /// </summary>
    public IReadOnlyDictionary<string, Version> MaxVersions { get; init; }
        = new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The filesystem path of the DLL that carries the maximum version for each
    /// shared assembly name. Keyed by simple assembly name (case-insensitive).
    /// </summary>
    public IReadOnlyDictionary<string, string> MaxSourcePaths { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Cross-plugin incompatibilities detected during scanning (case b of FR-15):
    /// two or more active plugin folders carry the same shared assembly at
    /// mutually-incompatible major versions. Each entry is a human-readable
    /// description of the conflict, suitable for logging.
    /// </summary>
    public IReadOnlyList<string> CrossPluginIncompatibilities { get; init; } = [];

    /// <summary>
    /// Returns the resolved max version for <paramref name="assemblyName"/>, or
    /// <see langword="null"/> if the assembly was not found in any scanned folder.
    /// </summary>
    public Version? GetMaxVersion(string assemblyName)
        => MaxVersions.TryGetValue(assemblyName, out var v) ? v : null;
}
