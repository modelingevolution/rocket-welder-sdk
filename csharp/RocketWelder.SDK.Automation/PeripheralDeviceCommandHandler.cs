using MicroPlumberd;
using MicroPlumberd.Services;
using RocketWelder.SDK.Abstractions;
using RocketWelder.SDK.Automation;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// Single generic command handler for all peripheral device commands.
/// Replaces the per-vendor handler dispatch pattern (no <c>Invoker&lt;T&gt;</c> needed —
/// there is exactly one aggregate type: <see cref="PeripheralDeviceConfigAggregate"/>).
///
/// <para>
/// The <c>InterfaceType</c> and <c>validKeys</c> are resolved from
/// <see cref="DeviceTypeRegistry"/> and passed into the aggregate's write methods so the
/// aggregate itself never references the registry.
/// </para>
/// </summary>
[CommandHandler]
public partial class PeripheralDeviceCommandHandler
{
    private readonly IPlumberInstance _plumber;
    private readonly PeripheralDevicesReadModel _readModel;
    private readonly DeviceTypeRegistry _typeRegistry;

    public PeripheralDeviceCommandHandler(
        IPlumberInstance plumber,
        PeripheralDevicesReadModel readModel,
        DeviceTypeRegistry typeRegistry)
    {
        _plumber = plumber;
        _readModel = readModel;
        _typeRegistry = typeRegistry;
    }

    [ThrowsFaultException<PeripheralDeviceFault>]
    public async Task Handle(DeviceId id, CreatePeripheralDevice cmd)
    {
        var typeInfo = _typeRegistry.Get(id.DeviceType)
            ?? throw new PeripheralDeviceFaultException(id, $"Unknown device type: {id.DeviceType}");

        // Validate name uniqueness within the same interface type
        var existing = _readModel.GetByInterface(typeInfo.InterfaceType)
            .FirstOrDefault(x => x.Name.Equals(cmd.Name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            throw new PeripheralDeviceFaultException(id, $"Device with name '{cmd.Name}' already exists for {typeInfo.InterfaceType}");

        var number = _readModel.GetNextNumber(typeInfo.InterfaceType);
        var validKeys = new HashSet<string>(typeInfo.PropertySchemas.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);

        var agg = await _plumber.Get<PeripheralDeviceConfigAggregate>(id);
        agg.Create(cmd.Name, number, cmd.Properties, typeInfo.InterfaceType, validKeys);
        await _plumber.SaveChanges(agg);
    }

    [ThrowsFaultException<PeripheralDeviceFault>]
    public async Task Handle(DeviceId id, RenamePeripheralDevice cmd)
    {
        var typeInfo = _typeRegistry.Get(id.DeviceType)
            ?? throw new PeripheralDeviceFaultException(id, $"Unknown device type: {id.DeviceType}");

        // Validate name uniqueness (allow same name on same device — no-op rename)
        var existing = _readModel.GetByInterface(typeInfo.InterfaceType)
            .FirstOrDefault(x => x.Name.Equals(cmd.Name, StringComparison.OrdinalIgnoreCase) && x.Id != id);
        if (existing != null)
            throw new PeripheralDeviceFaultException(id, $"Device with name '{cmd.Name}' already exists for {typeInfo.InterfaceType}");

        var agg = await _plumber.Get<PeripheralDeviceConfigAggregate>(id);
        agg.Rename(cmd.Name);
        await _plumber.SaveChanges(agg);
    }

    [ThrowsFaultException<PeripheralDeviceFault>]
    public async Task Handle(DeviceId id, ConfigurePeripheralDevice cmd)
    {
        var typeInfo = _typeRegistry.Get(id.DeviceType)
            ?? throw new PeripheralDeviceFaultException(id, $"Unknown device type: {id.DeviceType}");

        var validKeys = new HashSet<string>(typeInfo.PropertySchemas.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);

        var agg = await _plumber.Get<PeripheralDeviceConfigAggregate>(id);
        agg.Configure(cmd.Set, cmd.Unset, validKeys);
        await _plumber.SaveChanges(agg);
    }

    [ThrowsFaultException<PeripheralDeviceFault>]
    public async Task Handle(DeviceId id, RemovePeripheralDevice cmd)
    {
        // Type lookup not strictly required for Remove but keeps the error message consistent
        _ = _typeRegistry.Get(id.DeviceType)
            ?? throw new PeripheralDeviceFaultException(id, $"Unknown device type: {id.DeviceType}");

        var agg = await _plumber.Get<PeripheralDeviceConfigAggregate>(id);
        agg.Remove();
        await _plumber.SaveChanges(agg);
    }
}
