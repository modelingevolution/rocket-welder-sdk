using ModelingEvolution.Drawing;
using ModelingEvolution.Signals;
using RocketWelder.SDK.Abstractions;

namespace RocketWelder.SDK.Devices.Welding.Tests;

/// <summary>
/// Implements <see cref="IWeldingMachine"/> without overriding any of the wire-inch surface, so
/// <c>CanWireInch</c>/<c>WireInchSignal</c>/<c>WireInch</c>/<c>WireInchOn</c>/<c>WireInchOff</c>
/// resolve to the interface's default implementations (design.md "SDK — IWeldingMachine delta").
/// </summary>
internal sealed class UnsupportedWeldingMachine : IWeldingMachine
{
    private static WritableSignal<T> Signal<T>(string name) =>
        new(new SignalMetadata(name, new Uri($"signal://test/{name}"), null, null));

    public DeviceId Id { get; } = new("test-welder", Guid.Empty);
    public bool IsConnected { get; init; }

    public ISignal<Amps<float>> CurrentSignal { get; } = Signal<Amps<float>>("current");
    public ISignal<Amps<float>> TargetCurrentSignal { get; } = Signal<Amps<float>>("target-current");
    public ISignal<Speed<float>> WireFeedSpeedSignal { get; } = Signal<Speed<float>>("wire-feed-speed");
    public ISignal<Volts<float>> WeldingVoltageSignal { get; } = Signal<Volts<float>>("welding-voltage");

    public WritableSignal<WeldingMode> ModeSignal { get; } = Signal<WeldingMode>("mode");
    public WeldingMode Mode { get; set; }

    public WritableSignal<bool> WeldingStartSignal { get; } = Signal<bool>("welding-start");
    public bool WeldingStart { get; set; }

    public WritableSignal<bool> GasSignal { get; } = Signal<bool>("gas");
    public bool Gas { get; set; }

    public WritableSignal<int> JobNumberSignal { get; } = Signal<int>("job-number");
    public int JobNumber { get; set; }

    public ValueTask ArcOn() => throw new NotSupportedException();
    public ValueTask ArcOff() => ValueTask.CompletedTask;
    public ValueTask GasOn() => throw new NotSupportedException();
    public ValueTask GasOff() => ValueTask.CompletedTask;

    public void Dispose()
    {
    }
}
