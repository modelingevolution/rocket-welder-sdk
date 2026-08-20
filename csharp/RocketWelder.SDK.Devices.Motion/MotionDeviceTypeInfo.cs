using ModelingEvolution.Signals;
using RocketWelder.SDK.Abstractions;
using RocketWelder.SDK.Automation;

namespace RocketWelder.SDK.Devices.Motion;

/// <summary>
/// The registry entry a <b>motion</b> device plugin registers: a <see cref="DeviceTypeInfo"/> that
/// also carries the axis roster (FR-8).
///
/// <para>
/// <b>Why a subclass rather than a member on <see cref="DeviceTypeInfo"/>.</b> The roster is typed
/// by <see cref="AxisKind"/>, which belongs to the motion contract, and by
/// <see cref="ConfigPropertySchema"/>, which belongs to the plugin contract. Putting <c>Axes</c> on
/// <see cref="DeviceTypeInfo"/> itself would force <c>RocketWelder.SDK.Automation.Abstractions</c>
/// to reference this package — reversing the family's one-way flow and, if
/// <see cref="AxisDeclaration"/> moved with it, closing a reference cycle. Extending the record
/// instead keeps the dependency one-way (<c>Devices.Motion</c> → <c>Automation.Abstractions</c>)
/// and leaves the plugin contract <b>completely untouched</b>: no camera or welder plugin acquires
/// a motion dependency.
/// </para>
///
/// <para>
/// The registry stores and returns <see cref="DeviceTypeInfo"/> as before; a consumer that cares
/// about axes pattern-matches for it:
/// <code>
/// if (info is MotionDeviceTypeInfo m)
///     foreach (var axis in m.Axes) { /* render one dialog section per axis */ }
/// </code>
/// which is also the honest shape — most device types have no axes, and asking whether one does is
/// a type test rather than an empty-array check.
/// </para>
///
/// <para>
/// <b>Accepted cost.</b> Deriving from <see cref="DeviceTypeInfo"/> makes
/// <c>ModelingEvolution.Signals</c> a direct reference of this package (its <c>GetSignals</c> member
/// is typed by <c>ISignal&lt;float&gt;</c>) — measured, not assumed: it appears in
/// <c>GetReferencedAssemblies()</c> whether or not this type's own signature names it. It is not a
/// transport, so NFR-4 / AC-26 are unaffected, and <c>TransportDependencyTests</c> pins the full
/// direct-reference set so any further growth fails a test.
/// </para>
/// </summary>
/// <param name="DeviceType">The frozen device-type discriminator (FR-8).</param>
/// <param name="InterfaceType">The interface name the registry orders by.</param>
/// <param name="DisplayName">The human label shown in the Add-device dialog.</param>
/// <param name="InterfaceClrType">The device marker CLR type — for a motion device this is
/// <see cref="IPositioner"/> or <see cref="ILinearTrack"/>, which is what makes the marker the
/// primary classification (FR-3).</param>
/// <param name="PropertySchemas">Device-level configuration properties. Per-axis properties live on
/// the <see cref="Axes"/> declarations instead.</param>
/// <param name="Factory">Builds the device instance from its <c>ConfigSet</c>.</param>
/// <param name="GetSignals">Optional signal projection, as on <see cref="DeviceTypeInfo"/>.</param>
/// <param name="DetailView">Optional Blazor detail component.</param>
/// <param name="ParameterEditorView">Optional Blazor parameter-editor component.</param>
public record MotionDeviceTypeInfo(
    string DeviceType,
    string InterfaceType,
    string DisplayName,
    Type InterfaceClrType,
    ConfigPropertySchema[] PropertySchemas,
    Func<ConfigSet, DeviceId, object> Factory,
    Func<object, IEnumerable<ISignal<float>>>? GetSignals = null,
    Type? DetailView = null,
    Type? ParameterEditorView = null)
    : DeviceTypeInfo(DeviceType, InterfaceType, DisplayName, InterfaceClrType, PropertySchemas,
                     Factory, GetSignals, DetailView, ParameterEditorView)
{
    /// <summary>
    /// The device type's axis roster, in the order the plugin declared it. Defaults to empty so a
    /// motion device type that has not yet declared its axes is still constructible; a real motion
    /// plugin always sets it:
    /// <code>
    /// new MotionDeviceTypeInfo(…) { Axes = [new AxisDeclaration("tilt", AxisKind.Rotary, […])] }
    /// </code>
    /// </summary>
    public AxisDeclaration[] Axes { get; init; } = [];
}
