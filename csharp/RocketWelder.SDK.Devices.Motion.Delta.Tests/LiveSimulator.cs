using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Devices.Motion.Delta.Tests;

/// <summary>
/// The <c>delta-positioner-sim</c> instance these tests talk to over real Modbus TCP.
///
/// <para>
/// Start it with <c>./run.sh</c> in that repository: UI on :5185, tilt drive on :5502, turntable on
/// :5503. Override with <c>DELTA_SIM_HOST</c>, <c>DELTA_SIM_TILT_PORT</c> and
/// <c>DELTA_SIM_TURNTABLE_PORT</c>.
/// </para>
///
/// <para>
/// <b>What these tests may assert, and what they may not.</b> The simulator's coast model is the
/// programmed ramp alone and it runs about 7 % fast on long moves (26.8° modelled against 32.3°
/// measured at 50 Hz). Lead distance, braking margin and the continuous→pulse handover are therefore
/// tuned and validated <b>on the bench only</b>. Everything here asserts <i>logic</i>: the state
/// machine, which registers are written and in what order, the watchdog, the lease. Never physics
/// timing, and never a duration.
/// </para>
/// </summary>
internal static class LiveSimulator
{
    public static string Host => Environment.GetEnvironmentVariable("DELTA_SIM_HOST") ?? "127.0.0.1";

    public static int TiltPort => Port("DELTA_SIM_TILT_PORT", 5502);

    public static int TurntablePort => Port("DELTA_SIM_TURNTABLE_PORT", 5503);

    private static readonly Lazy<bool> Reachable = new(() => CanConnect(TiltPort) && CanConnect(TurntablePort));

    public static bool IsRunning => Reachable.Value;

    public static string SkipReason =>
        $"the Delta positioner simulator is not reachable at {Host}:{TiltPort}/{TurntablePort} — "
        + "start it with ./run.sh in delta-positioner-sim";

    /// <summary>
    /// The in-position band a move is allowed to settle within <b>when talking to the simulator</b>.
    ///
    /// <para>
    /// <b>This is not a machine number and must never travel back into
    /// <see cref="DeltaPositionerDefaults"/>.</b> The machine's own tolerances are ±0.05° on the
    /// turntable and ±0.10° on tilt, measured. The simulator's endgame is not the machine's: its
    /// micro-pulse produces roughly twice the travel the bench-measured pulse calibration predicts,
    /// because its coast model is the programmed ramp alone. Holding the adapter to a bench tolerance
    /// here would be measuring the simulator and calling it the machine — and the pressure to "fix"
    /// it would land on exactly the constants (lead distance, braking margin, handover) that are
    /// bench-only. The simulator repository's own smoke test loosens to the same 0.2° for the same
    /// reason.
    /// </para>
    /// </summary>
    public static Degree<double> SimulatorTolerance => Degree<double>.Create(0.2);

    /// <summary>Tilt as the simulator serves it: limited travel, limit switches, homing required.</summary>
    public static DeltaAxisConfig Tilt => DeltaPositionerDefaults.Tilt with
    {
        Host = Host, Port = TiltPort, Tolerance = SimulatorTolerance,
    };

    /// <summary>The turntable as the simulator serves it: wrapping, no limit switches.</summary>
    public static DeltaAxisConfig Turntable => DeltaPositionerDefaults.Turntable with
    {
        Host = Host, Port = TurntablePort, Tolerance = SimulatorTolerance,
    };

    /// <summary>A raw channel for observing the drive independently of the axis under test.</summary>
    public static ModbusChannel Observe(int port, ILogger? logger = null) => new(Host, port, logger);

    /// <summary>
    /// Puts the drive back into a state the next test can start from: watchdog latch cleared, lease
    /// released, motion stopped. The simulator has no reset endpoint, so this is done the way a real
    /// commander would — by writing the registers.
    /// </summary>
    public static async Task QuiesceAsync(int port)
    {
        using var channel = Observe(port);
        await channel.ConnectAsync(CancellationToken.None);
        await channel.WriteRegisterAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D110_Frequency, 0,
            "quiesce: frequency", ChannelPriority.Stop, CancellationToken.None);
        await channel.WriteCoilAsync(DeltaRegisters.PlcUnit, DeltaRegisters.M4_Move, false,
            "quiesce: motion coil", ChannelPriority.Stop, CancellationToken.None);
        await channel.WriteCoilAsync(DeltaRegisters.PlcUnit, DeltaRegisters.M6_ArmLatch, false,
            "quiesce: latch arm", ChannelPriority.Stop, CancellationToken.None);
        await channel.WriteRegisterAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D132_WatchdogFault,
            DeltaRegisters.WatchdogHealthy, "quiesce: watchdog latch", ChannelPriority.Stop,
            CancellationToken.None);
        await channel.WriteRegisterAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D131_OwnerId,
            AdvisoryLease.Unowned, "quiesce: lease", ChannelPriority.Stop, CancellationToken.None);
    }

    public static async Task<ushort> ReadAsync(ModbusChannel channel, byte unit, ushort address) =>
        (await channel.ReadHoldingAsync(unit, address, 1, "observe", ChannelPriority.Move,
            CancellationToken.None))[0];

    public static Task<bool[]> ReadCoilsAsync(ModbusChannel channel, ushort address, ushort count) =>
        channel.ReadCoilsAsync(DeltaRegisters.PlcUnit, address, count, "observe", ChannelPriority.Move,
            CancellationToken.None);

    public static Task<int> ReadDWordAsync(ModbusChannel channel, ushort address) =>
        channel.ReadDWordAsync(DeltaRegisters.PlcUnit, address, "observe", ChannelPriority.Move,
            CancellationToken.None);

    /// <summary>
    /// Waits until the drive's watchdog network has actually <b>armed</b>, which it does on the first
    /// CHANGE it observes in D130 — never merely on a client deciding to beat.
    ///
    /// <para>
    /// A kill-test that skips this proves nothing: the watchdog on a drive nobody has beaten at is
    /// disarmed by design (a drive with no commander has no commander to lose), so killing the
    /// process produces no trip and the test passes or fails for the wrong reason. Waiting for a real
    /// change in the register is also the only honest way to observe arming from outside — the
    /// ladder publishes no "armed" flag.
    /// </para>
    /// </summary>
    public static async Task<bool> WaitUntilArmedAsync(ModbusChannel observer, TimeSpan timeout)
    {
        var first = await ReadAsync(observer, DeltaRegisters.PlcUnit, DeltaRegisters.D130_Heartbeat);
        return await WaitUntilAsync(async () =>
            await ReadAsync(observer, DeltaRegisters.PlcUnit, DeltaRegisters.D130_Heartbeat) != first,
            timeout);
    }

    /// <summary>Waits for a condition, so a test never sleeps a guessed duration.</summary>
    public static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return true;
            await Task.Delay(50);
        }

        return await condition();
    }

    private static int Port(string variable, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(variable), out var port) ? port : fallback;

    private static bool CanConnect(int port)
    {
        try
        {
            using var probe = new TcpClient();
            return probe.ConnectAsync(Host, port).Wait(TimeSpan.FromSeconds(2)) && probe.Connected;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// A <see cref="FactAttribute"/> that skips when the simulator is not running, so the suite is
/// usable on a machine without it — and reports WHY rather than silently passing.
/// </summary>
public sealed class SimFactAttribute : FactAttribute
{
    public SimFactAttribute()
    {
        if (!LiveSimulator.IsRunning) Skip = LiveSimulator.SkipReason;
    }
}

/// <summary>
/// Every live-simulator test shares two physical drives, so they must not run at the same time.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LiveSimulatorCollection
{
    public const string Name = "live-simulator";
}

/// <summary>Shorthand for a typed angle in these tests.</summary>
internal static class Deg
{
    public static Degree<double> Of(double value) => Degree<double>.Create(value);
}
