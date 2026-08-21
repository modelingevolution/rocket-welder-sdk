using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelingEvolution.Drawing.Units;

namespace RocketWelder.SDK.Devices.Motion.Delta.Tests;

/// <summary>
/// A <see cref="DeltaAxis"/> wired to a <see cref="FakeDrive"/>: enough to exercise the state
/// machine, the register writes and the speed conversion without a socket, and deliberately not
/// enough to exercise motion (see <see cref="FakeDrive"/> for why).
/// </summary>
internal sealed class AxisTestBed : IDisposable
{
    private AxisTestBed(DeltaAxis axis, FakeDrive drive, DeltaAxisConfig config)
    {
        Axis = axis;
        Drive = drive;
        Config = config;
    }

    public DeltaAxis Axis { get; }

    public FakeDrive Drive { get; }

    public DeltaAxisConfig Config { get; }

    /// <summary>The turntable: wrapping, no limit switches, no homing required.</summary>
    public static AxisTestBed Turntable(Action<FakeDrive>? arrange = null) =>
        Build(DeltaPositionerDefaults.Turntable, arrange);

    /// <summary>The tilt axis: limited travel −45…+90°, homing required, limits on MI4/MI5.</summary>
    public static AxisTestBed Tilt(Action<FakeDrive>? arrange = null) =>
        Build(DeltaPositionerDefaults.Tilt, arrange);

    public static AxisTestBed Build(DeltaAxisConfig config, Action<FakeDrive>? arrange = null,
        IAxisStateStore? store = null, ILogger? logger = null)
    {
        var drive = new FakeDrive(config.Host);
        arrange?.Invoke(drive);
        var axis = new DeltaAxis(config, drive, store ?? new InMemoryAxisStateStore(),
            logger ?? NullLogger.Instance);
        return new AxisTestBed(axis, drive, config);
    }

    /// <summary>Powers the axis on, leaving it in <see cref="AxisState.Standstill"/>.</summary>
    public async Task<AxisTestBed> PoweredAsync()
    {
        await Axis.PowerAsync(true);
        return this;
    }

    /// <summary>
    /// Declares the axis homed at the given raw count, the way a completed homing run would, so a
    /// test can command an absolute move without running a whole homing sequence.
    /// </summary>
    public static async Task<AxisTestBed> HomedAsync(DeltaAxisConfig config, long zeroCount = 0,
        Action<FakeDrive>? arrange = null)
    {
        var store = new InMemoryAxisStateStore();
        await store.SaveAsync(config.Name, new AxisPersistedState(zeroCount, Homed: true, SpeedDegPerSecond: 0));

        var bed = Build(config, arrange, store);
        await bed.Axis.InitialiseAsync(CancellationToken.None);
        await bed.Axis.PowerAsync(true);
        return bed;
    }

    /// <summary>A typed angular speed in the axis's own unit.</summary>
    public static AngularSpeed<double, DegreePerSecond<double>> DegPerSecond(double value) => new(value);

    public void Dispose()
    {
        Axis.Dispose();
        Drive.Dispose();
    }
}
