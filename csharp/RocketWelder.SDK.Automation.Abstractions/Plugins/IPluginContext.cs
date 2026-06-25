namespace RocketWelder.SDK.Automation.Plugins;

/// <summary>
/// Passed to <see cref="IPlugin.Configure"/> after the DI container is built.
/// Gives each plugin access to the device type registry and the resolved service provider
/// so factory delegates can resolve ILogger, options, and other host services.
/// </summary>
public interface IPluginContext
{
    /// <summary>
    /// The host's device type registry. Call <c>Devices.Register(new DeviceTypeInfo(...))</c>
    /// to make a device type available for configuration and creation.
    /// </summary>
    DeviceTypeRegistry Devices { get; }

    /// <summary>
    /// The built DI service provider. Use this inside factory delegates to resolve
    /// <c>ILogger&lt;TDevice&gt;</c>, options, and other host services.
    /// </summary>
    IServiceProvider Services { get; }
}
