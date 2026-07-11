using RocketWelder.SDK.Automation.Plugins;

namespace RocketWelder.SDK.Automation.Plugins;

/// <summary>
/// Default implementation of <see cref="IPluginContext"/> passed to each plugin's
/// <see cref="IPlugin.Configure"/> call.
/// </summary>
internal sealed class PluginContext(DeviceTypeRegistry devices, IServiceProvider services) : IPluginContext
{
    /// <inheritdoc/>
    public DeviceTypeRegistry Devices { get; } = devices;

    /// <inheritdoc/>
    public IServiceProvider Services { get; } = services;
}
