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
    /// Live welding current as a signal. Consumers gate on <see cref="ISignal{T}.HasValue"/>
    /// before reading <see cref="ISignal{T}.Value"/> (<c>HasValue == false</c> when the device
    /// is unmapped or no read has succeeded yet), or call
    /// <see cref="ISignal{T}.Subscribe"/> to receive <c>Sample&lt;Amps&lt;float&gt;&gt;</c>
    /// events as each polled value arrives.
    /// </summary>
    ISignal<Amps<float>> Current { get; }

    /// <summary>Strike the arc. Returns when the hardware has acknowledged the command.</summary>
    ValueTask ArcOn();

    /// <summary>Extinguish the arc. Returns when the hardware has acknowledged the command.</summary>
    ValueTask ArcOff();
}
