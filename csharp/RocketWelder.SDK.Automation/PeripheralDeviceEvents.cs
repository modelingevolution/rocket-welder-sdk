using MicroPlumberd;
using RocketWelder.SDK.Automation;

namespace RocketWelder.SDK.Automation;

// ─────────────────────────────────────────────────────────────────────────────
// GUARDRAIL: These event record names are FROZEN — they are the persisted
// identifiers in the event store (MicroPlumberd keys on simple type name).
// DO NOT RENAME. DO NOT REMOVE [OutputStream("PeripheralDevice")].
// The automated replay test in RocketWelder.SDK.Automation.Tests verifies both.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Emitted when a peripheral device is created for the first time.</summary>
[OutputStream("PeripheralDevice")]
public record PeripheralDeviceCreated(string DeviceType, string InterfaceType, string Name, int Number)
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

/// <summary>Emitted when a peripheral device is renamed.</summary>
[OutputStream("PeripheralDevice")]
public record PeripheralDeviceRenamed(string Name)
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

/// <summary>Emitted when a peripheral device's configuration properties are changed.</summary>
[OutputStream("PeripheralDevice")]
public record PeripheralDeviceConfigured(ConfigSet Set, string[] Unset)
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

/// <summary>Emitted when a peripheral device is removed.</summary>
[OutputStream("PeripheralDevice")]
public record PeripheralDeviceRemoved
{
    public Guid Id { get; init; } = Guid.NewGuid();
}
