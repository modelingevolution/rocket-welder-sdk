using RocketWelder.SDK.Automation;
using RocketWelder.SDK.Automation.Plugins;

namespace RocketWelder.SDK.Devices.Positioner.Delta.Plugin;

/// <summary>
/// Registers the Delta VFD-C2000 welding positioner with the host.
///
/// <para>
/// Dropping this plugin's folder into the host's plugin directory is the whole integration — no host
/// source edit and no host rebuild.
/// </para>
/// </summary>
[RocketWelderPlugin("Delta Positioner")]
public sealed class DeltaPositionerPlugin : IPlugin
{
    /// <inheritdoc/>
    public void Configure(IPluginContext context) =>
        context.Devices.Register(new DeviceTypeInfo(
            DeviceType: DeltaPositionerRegistration.DeviceType,
            InterfaceType: nameof(IPositioner),
            DisplayName: "Delta VFD-C2000 Positioner (2 axes)",
            InterfaceClrType: typeof(IPositioner),
            PropertySchemas: DeltaPositionerRegistration.BuildSchemas(),
            Factory: (config, id) => DeltaPositionerRegistration.Build(config, id, context.Services)));
}
