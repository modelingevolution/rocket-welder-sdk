using MicroPlumberd;
using RocketWelder.SDK.Abstractions;
using RocketWelder.SDK.Automation;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// Single generic event-sourced config aggregate for ALL peripheral device types.
/// Replaces the per-vendor aggregate classes (FroniusWeldingMachineConfigAggregate, AbbConfigAggregate, …).
///
/// <para>
/// <b>GUARDRAIL</b>: the <c>[OutputStream("PeripheralDevice")]</c> attribute MUST stay — removing it
/// silently changes the stream category to <c>"PeripheralDeviceConfigAggregate"</c> and makes all
/// existing device streams unreadable. The automated replay test in
/// <c>RocketWelder.SDK.Automation.Tests</c> verifies this.
/// </para>
///
/// <para>
/// <b>Validation invariant (S3)</b>: validation (closed-key set) runs ONLY on the write/command path
/// (<see cref="Create"/>, <see cref="Configure"/>). The <c>Given</c> replay handlers are always
/// total and forward-compatible — they never validate and never reject unknown keys.
/// </para>
/// </summary>
[Aggregate]
[OutputStream("PeripheralDevice")]
public partial class PeripheralDeviceConfigAggregate(DeviceId id)
    : AggregateBase<DeviceId, PeripheralDeviceConfigAggregate.DeviceState>(id)
{
    /// <summary>Mutable state rebuilt from the event stream.</summary>
    public readonly record struct DeviceState
    {
        public bool IsCreated { get; init; }
        public bool IsRemoved { get; init; }
        public string Name { get; init; }
        public int Number { get; init; }
        public string InterfaceType { get; init; }
        public ConfigSet Config { get; init; }
    }

    /// <summary>Creates a new (version -1) aggregate instance for command dispatch or unit testing.</summary>
    public static PeripheralDeviceConfigAggregate New(DeviceId id) => new(id);

    // ── Replay handlers (Given) — total, forward-compatible, never validate ──

    private static DeviceState Given(DeviceState state, PeripheralDeviceCreated ev) => state with
    {
        IsCreated = true,
        Name = ev.Name,
        Number = ev.Number,
        InterfaceType = ev.InterfaceType,
        Config = ConfigSet.Empty
    };

    private static DeviceState Given(DeviceState state, PeripheralDeviceRenamed ev) => state with
    {
        Name = ev.Name
    };

    private static DeviceState Given(DeviceState state, PeripheralDeviceConfigured ev) =>
        state with { Config = state.Config.Apply(ev.Set, ev.Unset) };

    private static DeviceState Given(DeviceState state, PeripheralDeviceRemoved _) => state with
    {
        IsRemoved = true
    };

    // ── Write methods — validate, then emit ──

    /// <summary>
    /// Creates the device. Validates the initial <paramref name="properties"/> against
    /// <paramref name="validKeys"/> (derived from the device type's <see cref="ConfigPropertySchema"/>
    /// array registered in the <c>DeviceTypeRegistry</c>).
    /// <paramref name="interfaceType"/> is supplied by the command handler from
    /// <c>typeInfo.InterfaceType</c> — it is NOT on <see cref="DeviceId"/>.
    /// </summary>
    public void Create(string name, int number, ConfigSet properties, string interfaceType, IReadOnlySet<string> validKeys)
    {
        if (State.IsCreated)
            throw new InvalidOperationException("Device already created");

        ValidateProperties(properties, validKeys);

        AppendPendingChange(new PeripheralDeviceCreated(Id.DeviceType, interfaceType, name, number));

        if (properties.Count > 0)
            AppendPendingChange(new PeripheralDeviceConfigured(properties, []));
    }

    /// <summary>Renames the device.</summary>
    public void Rename(string newName)
    {
        EnsureAlive();
        if (State.Name == newName) return;
        AppendPendingChange(new PeripheralDeviceRenamed(newName));
    }

    /// <summary>
    /// Applies a config patch. Validates the keys in <paramref name="set"/> and
    /// <paramref name="unset"/> against <paramref name="validKeys"/> on the write path.
    /// </summary>
    public void Configure(ConfigSet set, string[] unset, IReadOnlySet<string> validKeys)
    {
        EnsureAlive();
        ValidateProperties(set, validKeys);
        ValidateUnsetKeys(unset, validKeys);

        if (set.Count == 0 && unset.Length == 0) return;

        AppendPendingChange(new PeripheralDeviceConfigured(set, unset));
    }

    /// <summary>Marks the device as removed.</summary>
    public void Remove()
    {
        EnsureAlive();
        AppendPendingChange(new PeripheralDeviceRemoved());
    }

    // ── Helpers ──

    private void EnsureAlive()
    {
        if (!State.IsCreated)
            throw new InvalidOperationException("Device not created");
        if (State.IsRemoved)
            throw new InvalidOperationException("Device already removed");
    }

    private static void ValidateProperties(ConfigSet props, IReadOnlySet<string> validKeys)
    {
        if (validKeys.Count == 0) return; // no schema = accept all

        foreach (var (name, _) in props)
        {
            // "tag.*" dynamic keys are always allowed (for Modbus tag-mapped devices)
            if (name.StartsWith("tag.", StringComparison.OrdinalIgnoreCase)) continue;
            if (!validKeys.Contains(name))
                throw new ArgumentException($"Unknown property key '{name}'. Valid: {string.Join(", ", validKeys)}");
        }
    }

    private static void ValidateUnsetKeys(string[] unset, IReadOnlySet<string> validKeys)
    {
        if (validKeys.Count == 0 || unset.Length == 0) return;

        foreach (var name in unset)
        {
            if (name.StartsWith("tag.", StringComparison.OrdinalIgnoreCase)) continue;
            if (!validKeys.Contains(name))
                throw new ArgumentException($"Cannot unset unknown property key '{name}'");
        }
    }
}
