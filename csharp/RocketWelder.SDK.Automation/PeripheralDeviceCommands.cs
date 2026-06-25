using RocketWelder.SDK.Abstractions;
using RocketWelder.SDK.Automation;

namespace RocketWelder.SDK.Automation;

/// <summary>Creates a new peripheral device of the type encoded in <see cref="DeviceId.DeviceType"/>.</summary>
public record CreatePeripheralDevice(DeviceId DeviceId, string Name, ConfigSet Properties)
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

/// <summary>Renames an existing peripheral device.</summary>
public record RenamePeripheralDevice(DeviceId DeviceId, string Name)
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

/// <summary>Applies a config-set patch (add/replace <see cref="Set"/>, remove <see cref="Unset"/> keys) to a device.</summary>
public record ConfigurePeripheralDevice(DeviceId DeviceId, ConfigSet Set, string[] Unset)
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

/// <summary>Removes a peripheral device (soft-deletes its stream).</summary>
public record RemovePeripheralDevice(DeviceId DeviceId)
{
    public Guid Id { get; init; } = Guid.NewGuid();
}
