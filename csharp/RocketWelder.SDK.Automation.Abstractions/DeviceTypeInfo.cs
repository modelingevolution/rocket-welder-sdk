using ModelingEvolution.Signals;
using RocketWelder.SDK.Abstractions;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// Describes a registered peripheral device type (e.g., Fronius TPS5000, Fairino robot, camera).
///
/// <para>
/// <b>No <c>AggregateType</c>.</b> The host owns the single generic
/// <c>PeripheralDeviceConfigAggregate</c>; the plugin never names it.
/// This is what keeps the plugin free of any MicroPlumberd / event-sourcing dependency.
/// </para>
///
/// <para>
/// <b><see cref="GetSignals"/>.</b> When non-null, the host's read model invokes this after
/// constructing a device instance via <see cref="Factory"/> to publish the device's signals
/// to the <c>IDeviceSignalPublisher</c> port. The factory wires it as:
/// <c>GetSignals = d => ((MyDevice)d).Signals</c> where <c>Signals</c> is a plain
/// <c>IEnumerable&lt;ISignal&lt;float&gt;&gt;</c> on the adapter (catalog-ready projections).
/// </para>
///
/// <para>
/// <b><see cref="DetailView"/> / <see cref="ParameterEditorView"/>.</b> Optional Blazor component
/// types rendered via the host's <c>DynamicComponent</c> seam. Devices without a rich panel
/// leave these <see langword="null"/>.
/// </para>
///
/// <para>Instances are ordered by <see cref="InterfaceType"/> then <see cref="DisplayName"/>
/// in the registry (see <see cref="DeviceTypeRegistry.Items"/>).</para>
/// </summary>
public record DeviceTypeInfo(
    string DeviceType,
    string InterfaceType,
    string DisplayName,
    Type InterfaceClrType,
    ConfigPropertySchema[] PropertySchemas,
    Func<ConfigSet, DeviceId, object> Factory,
    Func<object, IEnumerable<ISignal<float>>>? GetSignals = null,
    Type? DetailView = null,
    Type? ParameterEditorView = null) : IComparable<DeviceTypeInfo>
{
    /// <inheritdoc/>
    public int CompareTo(DeviceTypeInfo? other)
    {
        if (other is null) return 1;
        int cmp = string.Compare(InterfaceType, other.InterfaceType, StringComparison.OrdinalIgnoreCase);
        return cmp != 0 ? cmp : string.Compare(DisplayName, other.DisplayName, StringComparison.OrdinalIgnoreCase);
    }
}
