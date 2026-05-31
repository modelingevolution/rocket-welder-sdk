using ModelingEvolution.Drawing;
using ModelingEvolution.Signals;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// Welding-machine device surface. Combines two concerns:
/// <list type="bullet">
/// <item>
/// <b>Telemetry</b> — <see cref="Current"/> exposes the live welding current as an
/// <see cref="ISignal{T}"/> per the cross-cutting Signal Model
/// (<c>docs/epics/cross-cutting/signal-model.md</c>) and Epic 028 design.md §4. Consumers gate
/// on <c>Current.HasValue</c> before reading <c>Current.Value</c>, or call
/// <c>Current.Subscribe</c> to receive <c>Sample&lt;Amps&lt;float&gt;&gt;</c> events.
/// </item>
/// <item>
/// <b>Commands</b> — <see cref="ArcOn"/> / <see cref="ArcOff"/> are async write operations
/// against the welder hardware, used by generated <c>IProgram</c> source.
/// </item>
/// </list>
/// </summary>
public interface IWeldingMachine : IDevice
{
    /// <summary>
    /// Live (measured) welding current as a signal. Consumers gate on
    /// <see cref="ISignal{T}.HasValue"/> before reading <see cref="ISignal{T}.Value"/>
    /// (<c>HasValue == false</c> when the device is unmapped or no read has succeeded yet),
    /// or call <see cref="ISignal{T}.Subscribe"/> to receive
    /// <c>Sample&lt;Amps&lt;float&gt;&gt;</c> events as each polled value arrives.
    /// </summary>
    ISignal<Amps<float>> Current { get; }

    /// <summary>
    /// Target / commanded welding current (setpoint or current-guide depending on vendor).
    /// Same <see cref="ISignal{T}"/> semantics as <see cref="Current"/>; <c>HasValue == false</c>
    /// for welders that don't expose a setpoint surface or until the first polled value.
    /// </summary>
    ISignal<Amps<float>> TargetCurrent { get; }

    /// <summary>
    /// Wire feed speed, mm/min. Read-only on the interface — vendors decide whether they also expose
    /// a writable. <c>HasValue == false</c> when the welder is in a mode where wire feed is
    /// meaningless (TIG, MMA) or until the first polled value.
    /// </summary>
    ISignal<Speed<float>> WireFeedSpeed { get; }

    /// <summary>
    /// Measured arc voltage, V. Updates while welding; reads zero between arcs.
    /// Same <see cref="ISignal{T}"/> semantics as <see cref="Current"/>.
    /// </summary>
    ISignal<Volts<float>> WeldingVoltage { get; }

    /// <summary>
    /// Active welding mode. Writable — operators may change mode from our UI. Vendors whose hardware
    /// does not accept remote mode-write MUST still implement this and either accept the value into a
    /// local cache then reject on the welder side (the signal converges back on the next read tick)
    /// OR throw <see cref="System.NotSupportedException"/> from the underlying
    /// <c>ISignalSink.Set</c>. Drives per-vendor parameter visibility on the UI side and the
    /// adapter's own decisions about which registers to read/write.
    /// </summary>
    WritableSignal<WeldingMode> Mode { get; }

    /// <summary>
    /// Arc-active state. Writable — set <c>true</c> to start welding, <c>false</c> to stop. Parallel
    /// to <see cref="ArcOn"/> / <see cref="ArcOff"/> (which remain for ergonomic one-shot use); the
    /// signal form lets the catalog/oscilloscope pick it up as a boolean trace and lets the UI render
    /// a toggle bound to the same source of truth.
    /// </summary>
    WritableSignal<bool> WeldingStart { get; }

    /// <summary>Strike the arc. Returns when the hardware has acknowledged the command.</summary>
    ValueTask ArcOn();

    /// <summary>Extinguish the arc. Returns when the hardware has acknowledged the command.</summary>
    ValueTask ArcOff();
}
