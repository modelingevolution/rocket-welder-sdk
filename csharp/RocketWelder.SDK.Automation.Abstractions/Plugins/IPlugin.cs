using Microsoft.Extensions.DependencyInjection;

namespace RocketWelder.SDK.Automation.Plugins;

/// <summary>
/// Two-phase plugin lifecycle contract. Decorate the implementing class with
/// <see cref="RocketWelderPluginAttribute"/> so the host's loader can discover it.
///
/// <para>
/// <b>Phase 1</b> (<see cref="ConfigureServices"/>) runs BEFORE <c>BuildServiceProvider()</c>.
/// Add plugin-owned services here: <c>ILogger&lt;TDevice&gt;</c>, options, any bespoke
/// MicroPlumberd command handlers, etc. Default no-op via default interface member —
/// simple plugins need only implement <see cref="Configure"/>.
/// </para>
///
/// <para>
/// <b>Phase 2</b> (<see cref="Configure"/>) runs AFTER the container is built.
/// Register device types + views into <see cref="IPluginContext.Devices"/>. This is where
/// the body of the former <c>DeviceTypeRegistrations.cs</c> god-method now lives, one plugin
/// at a time. Resolved services (loggers, options) are available via
/// <see cref="IPluginContext.Services"/>.
/// </para>
/// </summary>
public interface IPlugin
{
    /// <summary>
    /// Phase 1 — optional. Called before <c>BuildServiceProvider()</c>.
    /// Override to register plugin-owned services into the DI container.
    /// Default: no-op.
    /// </summary>
    void ConfigureServices(IServiceCollection services) { }

    /// <summary>
    /// Phase 2 — required. Called after the DI container is built.
    /// Register device types (and their views) via <see cref="IPluginContext.Devices"/>.
    /// </summary>
    void Configure(IPluginContext context);
}
