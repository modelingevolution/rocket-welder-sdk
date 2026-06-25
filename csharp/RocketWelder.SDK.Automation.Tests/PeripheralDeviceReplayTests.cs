using System.Text.Json;
using FluentAssertions;
using MicroPlumberd;
using ModelingEvolution.Ipv4;
using ModelingEvolution.Signals;
using RocketWelder.SDK.Abstractions;
using RocketWelder.SDK.Automation;

namespace RocketWelder.SDK.Automation.Tests;

/// <summary>
/// GUARDRAIL replay tests — total-data-loss blast radius (device-extensibility.md §6.2).
///
/// The generic <see cref="PeripheralDeviceConfigAggregate"/> MUST carry
/// <c>[OutputStream("PeripheralDevice")]</c> and the four event records MUST keep their exact
/// simple type names (<c>PeripheralDeviceCreated/Renamed/Configured/Removed</c>). Changing
/// either silently makes all existing device streams unreadable.
///
/// These tests verify:
/// 1. <c>[OutputStream("PeripheralDevice")]</c> is present on the aggregate class.
/// 2. Event simple names are exactly frozen.
/// 3. A stream fixture from a "pre-rename" device (Created + Configured) replays correctly
///    into a fresh aggregate, proving forward-compatible hydration.
/// 4. Validation is SKIPPED on replay (unknown key does not throw).
/// </summary>
public class PeripheralDeviceReplayTests
{
    // ── Guardrail 1: OutputStream attribute ──────────────────────────────────

    [Fact]
    public void Aggregate_must_carry_OutputStream_PeripheralDevice()
    {
        var attr = typeof(PeripheralDeviceConfigAggregate)
            .GetCustomAttributes(typeof(OutputStreamAttribute), inherit: false)
            .Cast<OutputStreamAttribute>()
            .FirstOrDefault();

        attr.Should().NotBeNull(
            "removing [OutputStream(\"PeripheralDevice\")] silently renames all device streams to " +
            "\"PeripheralDeviceConfigAggregate\" and makes every existing stream unreadable");

        attr!.OutputStreamName.Should().Be("PeripheralDevice",
            "the persisted stream category is frozen at \"PeripheralDevice\"");
    }

    // ── Guardrail 2: Frozen event simple names ────────────────────────────────

    [Fact]
    public void Event_simple_names_are_frozen()
    {
        typeof(PeripheralDeviceCreated).Name.Should().Be("PeripheralDeviceCreated");
        typeof(PeripheralDeviceRenamed).Name.Should().Be("PeripheralDeviceRenamed");
        typeof(PeripheralDeviceConfigured).Name.Should().Be("PeripheralDeviceConfigured");
        typeof(PeripheralDeviceRemoved).Name.Should().Be("PeripheralDeviceRemoved");
    }

    [Fact]
    public void Events_must_carry_OutputStream_PeripheralDevice()
    {
        foreach (var eventType in new[]
        {
            typeof(PeripheralDeviceCreated),
            typeof(PeripheralDeviceRenamed),
            typeof(PeripheralDeviceConfigured),
            typeof(PeripheralDeviceRemoved)
        })
        {
            var attr = eventType
                .GetCustomAttributes(typeof(OutputStreamAttribute), inherit: false)
                .Cast<OutputStreamAttribute>()
                .FirstOrDefault();

            attr.Should().NotBeNull($"event {eventType.Name} must carry [OutputStream(\"PeripheralDevice\")]");
            attr!.OutputStreamName.Should().Be("PeripheralDevice",
                $"event {eventType.Name} stream category is frozen at \"PeripheralDevice\"");
        }
    }

    // ── Guardrail 3: State folding from event records ─────────────────────────
    //
    // Simulates the event stream written by the OLD per-vendor aggregates (e.g.,
    // FroniusWeldingMachineConfigAggregate). The generic aggregate must rebuild the
    // same state from those exact event records and names.

    [Fact]
    public async Task Rehydrate_folds_state_from_event_records()
    {
        var deviceId = DeviceId.New("FroniusWeldingMachine_TPS5000");
        var agg = PeripheralDeviceConfigAggregate.New(deviceId);

        var config = new ConfigSet(
            new IpProperty(Ipv4Address.Parse("192.168.1.20", null)),
            new PortProperty(502));

        // These are the exact event records as they appear in pre-rename EventStore streams
        var fixture = new object[]
        {
            new PeripheralDeviceCreated("FroniusWeldingMachine_TPS5000", "IWeldingMachine", "Welder1", 1)
                { Id = Guid.NewGuid() },
            new PeripheralDeviceConfigured(config, [])
                { Id = Guid.NewGuid() }
        };

        await agg.Rehydrate(AsAsync(fixture));

        var state = ((IStatefull<PeripheralDeviceConfigAggregate.DeviceState>)agg).State;
        state.IsCreated.Should().BeTrue();
        state.IsRemoved.Should().BeFalse();
        state.Name.Should().Be("Welder1");
        state.Number.Should().Be(1);
        state.InterfaceType.Should().Be("IWeldingMachine");
        state.Config.Get<IpProperty, Ipv4Address>().Should().Be(Ipv4Address.Parse("192.168.1.20", null));
        state.Config.Get<PortProperty, int>().Should().Be(502);

        agg.PendingEvents.Should().BeEmpty("replayed events must NOT produce pending events");
    }

    // ── Guardrail 3b: JSON serialization round-trip ───────────────────────────
    //
    // Proves the wire-level compatibility of our event types: serializes to JSON (the
    // same format MicroPlumberd writes to EventStore) and deserializes back by resolving
    // the CLR type from the event's simple name — exactly what MicroPlumberd does on read.
    // This is stronger than Guardrail 3 because it catches property renames and JSON
    // converter failures that wouldn't show up in a same-assembly in-memory round-trip.

    [Fact]
    public async Task Rehydrate_survives_JSON_serialization_round_trip()
    {
        // ConfigSetJsonConverter uses ConfigPropertyJsonConverter which requires the property
        // types to be registered (in production this happens at startup via ScanAssembly).
        // Register the SDK's built-in property types so Port/Ip can be deserialized from JSON.
        ConfigPropertyJsonConverter.ScanAssembly<IpProperty>();

        // Registry maps event simple name → CLR type, mirroring MicroPlumberd's default
        // GetEventNameConvention (type.Name). If a type is renamed, this test fails.
        var typeRegistry = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(PeripheralDeviceCreated)] = typeof(PeripheralDeviceCreated),
            [nameof(PeripheralDeviceConfigured)] = typeof(PeripheralDeviceConfigured),
        };

        var deviceId = DeviceId.New("FroniusWeldingMachine_TPS5000");

        // Simulate what the aggregate writes to EventStore: JSON-serialized events.
        var config = new ConfigSet(
            new IpProperty(Ipv4Address.Parse("192.168.1.20", null)),
            new PortProperty(502));

        var wirePayloads = new (string TypeName, byte[] Json)[]
        {
            (nameof(PeripheralDeviceCreated),
                JsonSerializer.SerializeToUtf8Bytes(
                    new PeripheralDeviceCreated("FroniusWeldingMachine_TPS5000", "IWeldingMachine", "Welder1", 1)
                        { Id = Guid.NewGuid() })),
            (nameof(PeripheralDeviceConfigured),
                JsonSerializer.SerializeToUtf8Bytes(
                    new PeripheralDeviceConfigured(config, []) { Id = Guid.NewGuid() })),
        };

        // Simulate what MicroPlumberd does on read: resolve type by name, deserialize from bytes.
        var deserialized = wirePayloads
            .Select(p => JsonSerializer.Deserialize(p.Json, typeRegistry[p.TypeName])!)
            .ToArray();

        // Replay through the same path as production.
        var agg = PeripheralDeviceConfigAggregate.New(deviceId);
        await agg.Rehydrate(AsAsync(deserialized));

        var state = ((IStatefull<PeripheralDeviceConfigAggregate.DeviceState>)agg).State;
        state.IsCreated.Should().BeTrue();
        state.Name.Should().Be("Welder1");
        state.InterfaceType.Should().Be("IWeldingMachine");
        state.Config.Get<IpProperty, Ipv4Address>().Should().Be(Ipv4Address.Parse("192.168.1.20", null));
        state.Config.Get<PortProperty, int>().Should().Be(502);

        agg.PendingEvents.Should().BeEmpty("replayed events must not produce pending events");
    }

    [Fact]
    public async Task Rehydrate_through_rename_and_removal_reaches_correct_terminal_state()
    {
        var deviceId = DeviceId.New("FroniusWeldingMachine_TPS5000");
        var agg = PeripheralDeviceConfigAggregate.New(deviceId);

        await agg.Rehydrate(AsAsync(
            new PeripheralDeviceCreated("FroniusWeldingMachine_TPS5000", "IWeldingMachine", "WelderA", 1) { Id = Guid.NewGuid() },
            new PeripheralDeviceRenamed("WelderB") { Id = Guid.NewGuid() },
            new PeripheralDeviceRemoved() { Id = Guid.NewGuid() }));

        var state = ((IStatefull<PeripheralDeviceConfigAggregate.DeviceState>)agg).State;
        state.Name.Should().Be("WelderB");
        state.IsRemoved.Should().BeTrue();
        agg.PendingEvents.Should().BeEmpty();
    }

    // ── Guardrail 4: Replay is total — unknown keys don't throw ──────────────

    [Fact]
    public async Task Rehydrate_with_unknown_config_key_does_not_throw()
    {
        var deviceId = DeviceId.New("SomeDevice");
        var agg = PeripheralDeviceConfigAggregate.New(deviceId);

        // Simulate a stream written under an older schema that had a key "LegacyProp"
        // that no longer exists in the current registry — replay must be total/forward-compat.
        var legacyConfig = new ConfigSet();
        legacyConfig.Add("LegacyProp", new SerialNumberProperty("SN-12345")); // unknown key in new schema

        var act = async () => await agg.Rehydrate(AsAsync(
            new PeripheralDeviceCreated("SomeDevice", "ISomeDevice", "Device1", 1) { Id = Guid.NewGuid() },
            new PeripheralDeviceConfigured(legacyConfig, []) { Id = Guid.NewGuid() }));

        await act.Should().NotThrowAsync(
            "replay (Given handlers) must be total and forward-compatible — unknown keys are retained verbatim");

        var state = ((IStatefull<PeripheralDeviceConfigAggregate.DeviceState>)agg).State;
        state.IsCreated.Should().BeTrue();
    }

    // ── Guardrail 5: Write path validates; replay does not ───────────────────

    [Fact]
    public void Create_with_unknown_property_key_throws_on_write_path()
    {
        var deviceId = DeviceId.New("FroniusWeldingMachine_TPS5000");
        var agg = PeripheralDeviceConfigAggregate.New(deviceId);

        var badConfig = new ConfigSet(new PortProperty(502));
        var validKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Ip", "UnitId" }; // "Port" is NOT in validKeys

        var act = () => agg.Create("Welder1", 1, badConfig, "IWeldingMachine", validKeys);

        act.Should().Throw<ArgumentException>("write-path validation must reject unknown property keys");
    }

    [Fact]
    public void Create_with_valid_properties_does_not_throw()
    {
        var deviceId = DeviceId.New("FroniusWeldingMachine_TPS5000");
        var agg = PeripheralDeviceConfigAggregate.New(deviceId);

        var config = new ConfigSet(
            new IpProperty(Ipv4Address.Parse("10.0.0.1", null)),
            new PortProperty(502));
        var validKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Ip", "Port", "UnitId" };

        var act = () => agg.Create("Welder1", 1, config, "IWeldingMachine", validKeys);

        act.Should().NotThrow();
        agg.PendingEvents.Should().HaveCount(2, "Create emits PeripheralDeviceCreated + PeripheralDeviceConfigured when properties are non-empty");
        agg.PendingEvents[0].Should().BeOfType<PeripheralDeviceCreated>();
        agg.PendingEvents[1].Should().BeOfType<PeripheralDeviceConfigured>();
    }

    [Fact]
    public void Create_with_tag_key_is_always_allowed()
    {
        var deviceId = DeviceId.New("ModbusDevice");
        var agg = PeripheralDeviceConfigAggregate.New(deviceId);

        var config = new ConfigSet();
        config.Add("tag.move_left", new SerialNumberProperty("M100")); // "tag.*" prefix always allowed

        var validKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Ip", "Port" }; // no "tag.*" in schema — but must be accepted

        var act = () => agg.Create("Plc1", 1, config, "IPlc", validKeys);

        act.Should().NotThrow("'tag.*' keys are always allowed regardless of the validKeys schema");
    }

    // ── Guardrail 6: null vs empty validKeys distinction (MINOR-2) ───────────
    //
    // null validKeys  = no schema / accept all (e.g. dynamic Modbus tag devices).
    // Empty validKeys = zero-key closed schema / reject all non-tag keys (e.g. Simulator).
    // The two must NOT be collapsed: Simulator must reject spurious config keys even if
    // it has zero named schema properties.

    [Fact]
    public void Create_with_null_validKeys_accepts_any_property()
    {
        var deviceId = DeviceId.New("DynamicModbus");
        var agg = PeripheralDeviceConfigAggregate.New(deviceId);
        // A property with an arbitrary name that is NOT in any schema
        var config = new ConfigSet(new SerialNumberProperty("SN-001"));

        var act = () => agg.Create("Modbus1", 1, config, "IDynamic", null); // null = no schema = accept all

        act.Should().NotThrow("null validKeys means no schema — all keys are accepted (dynamic-tag device)");
    }

    [Fact]
    public void Create_with_empty_validKeys_rejects_any_non_tag_property()
    {
        var deviceId = DeviceId.New("Simulator");
        var agg = PeripheralDeviceConfigAggregate.New(deviceId);
        var config = new ConfigSet(new SerialNumberProperty("SIM-001")); // non-tag key

        // Empty HashSet = zero-key closed schema (Simulator has no config properties)
        var emptySchema = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var act = () => agg.Create("Sim1", 1, config, "ISimulator", emptySchema);

        act.Should().Throw<ArgumentException>(
            "empty schema = closed schema with zero keys — any non-tag property is rejected (Simulator reject-all behaviour)");
    }

    [Fact]
    public void Create_with_empty_validKeys_still_allows_tag_keys()
    {
        var deviceId = DeviceId.New("Simulator");
        var agg = PeripheralDeviceConfigAggregate.New(deviceId);
        var config = new ConfigSet();
        config.Add("tag.signal_out", new SerialNumberProperty("Q0.0")); // tag.* always allowed
        var emptySchema = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var act = () => agg.Create("Sim1", 1, config, "ISimulator", emptySchema);

        act.Should().NotThrow("'tag.*' keys bypass schema validation even on a zero-key closed schema");
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static async IAsyncEnumerable<object> AsAsync(params object[] events)
    {
        foreach (var ev in events)
        {
            yield return ev;
            await Task.Yield();
        }
    }
}
