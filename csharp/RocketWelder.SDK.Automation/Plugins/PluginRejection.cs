namespace RocketWelder.SDK.Automation.Plugins;

/// <summary>
/// Describes a plugin that was rejected during load due to a version-alignment
/// incompatibility (FR-15). See device-extensibility.md §6.8 for the rejection
/// rules (highest-version-wins; only genuinely incompatible majors are rejected).
/// </summary>
/// <param name="PluginDllPath">Absolute path to the plugin DLL that was rejected.</param>
/// <param name="AssemblyName">Simple name of the shared assembly whose version is incompatible.</param>
/// <param name="RequiredVersion">Version the plugin's folder physically carries (its "required" version).</param>
/// <param name="ResolvedMaxVersion">The resolved process-wide maximum for that shared assembly.</param>
/// <param name="Message">
/// Human-readable rejection reason naming the plugin, assembly, and both versions.
/// Suitable for the startup log and the Plugins management page.
/// </param>
public sealed record PluginRejection(
    string PluginDllPath,
    string AssemblyName,
    Version RequiredVersion,
    Version ResolvedMaxVersion,
    string Message);
