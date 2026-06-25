using MicroPlumberd.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// DI extension methods for the peripheral-device lifecycle engine.
/// </summary>
public static class PeripheralDeviceServiceExtensions
{
    /// <summary>
    /// Registers the peripheral-device engine: type registry, generic aggregate command handler,
    /// read model, and a no-op signal publisher (replace with a catalog adapter in the host).
    ///
    /// <para>
    /// Call order in the host startup:
    /// <code>
    /// services.AddPeripheralDevices();
    /// // ... Phase 1: foreach plugin: plugin.ConfigureServices(services)
    /// var sp = services.BuildServiceProvider();
    /// // ... Phase 2: foreach plugin: plugin.Configure(new PluginContext(registry, sp))
    /// </code>
    /// </para>
    /// </summary>
    public static IServiceCollection AddPeripheralDevices(this IServiceCollection services)
    {
        services.AddSingleton<DeviceTypeRegistry>();
        services.AddSingletonCommandHandler<PeripheralDeviceCommandHandler>();
        services.AddSingletonEventHandler<PeripheralDevicesReadModel>();

        // No-op publisher is the default; hosts override by registering their own IDeviceSignalPublisher
        // BEFORE calling AddPeripheralDevices, or by using TryAdd here (first registration wins).
        services.TryAddSingleton<IDeviceSignalPublisher, NoOpDeviceSignalPublisher>();

        return services;
    }
}
