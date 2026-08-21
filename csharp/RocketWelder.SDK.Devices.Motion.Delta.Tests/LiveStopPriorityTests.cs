using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ModelingEvolution.Drawing.Units;

namespace RocketWelder.SDK.Devices.Motion.Delta.Tests;

/// <summary>
/// NFR-5 / AC-23: <b>the stop reaches the drive within 200 ms of the call</b>, measured during a long
/// move and during homing.
///
/// <para>
/// This is a transport-scheduling measurement, not a physics one — the clock runs from
/// <c>StopAsync</c> to the first stop write leaving on the wire, and stops there. How long the axis
/// then takes to decelerate is the drive's ramp and the machine's inertia, and is a bench number
/// this suite never touches. Without the stop lane the figure is unbounded: a 26 s homing hold would
/// queue ahead of it.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
[Collection(LiveSimulatorCollection.Name)]
public class LiveStopPriorityTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(200);

    [SimFact]
    public async Task StopReachesTheWireInsideTheBudget_DuringAContinuousMove()
    {
        await LiveSimulator.QuiesceAsync(LiveSimulator.TurntablePort);
        using var channel = new StopWatchingChannel(
            new ModbusChannel(LiveSimulator.Host, LiveSimulator.TurntablePort, NullLogger.Instance));
        await channel.ConnectAsync(CancellationToken.None);

        using var axis = new DeltaAxis(LiveSimulator.Turntable, channel, new InMemoryAxisStateStore(),
            NullLogger.Instance);
        await axis.PowerAsync(true);
        await axis.MoveVelocityAsync(new AngularSpeed<double, DegreePerSecond<double>>(10));

        var elapsed = await channel.TimeFirstStopWriteAsync(() => axis.StopAsync());

        elapsed.Should().BeLessThan(Budget);
    }

    [SimFact]
    public async Task StopReachesTheWireInsideTheBudget_DuringHoming()
    {
        // The case the priority lane exists for. Homing holds the channel across long jogs, and on a
        // plain FIFO lock a stop would wait behind the whole sequence.
        await LiveSimulator.QuiesceAsync(LiveSimulator.TurntablePort);
        using var channel = new StopWatchingChannel(
            new ModbusChannel(LiveSimulator.Host, LiveSimulator.TurntablePort, NullLogger.Instance));
        await channel.ConnectAsync(CancellationToken.None);

        using var axis = new DeltaAxis(LiveSimulator.Turntable, channel, new InMemoryAxisStateStore(),
            NullLogger.Instance);
        await axis.PowerAsync(true);

        var homing = axis.HomeAsync(new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token);
        (await LiveSimulator.WaitUntilAsync(() => Task.FromResult(axis.State == AxisState.Homing),
            TimeSpan.FromSeconds(10))).Should().BeTrue();
        await Task.Delay(500);   // let homing get properly under way and hold the channel

        var elapsed = await channel.TimeFirstStopWriteAsync(() => axis.StopAsync());
        elapsed.Should().BeLessThan(Budget);

        try { await homing; } catch (OperationCanceledException) { /* the stop cancelled it */ }
        catch (MotionException) { /* homing could not complete after a stop; that is the point */ }
    }

    /// <summary>
    /// Times the first zero written to the frequency register after a given call — the exact event
    /// AC-23 is worded against ("first stop write on the wire").
    /// </summary>
    private sealed class StopWatchingChannel(ModbusChannel inner) : IModbusChannel
    {
        private TaskCompletionSource<TimeSpan>? _pending;
        private long _startedAt;

        public async Task<TimeSpan> TimeFirstStopWriteAsync(Func<Task> call)
        {
            var pending = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
            _startedAt = Stopwatch.GetTimestamp();
            Volatile.Write(ref _pending, pending);

            var running = call();
            var elapsed = await pending.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await running;
            return elapsed;
        }

        private void NoteIfStopWrite(ushort address, ushort value)
        {
            if (address != DeltaRegisters.D110_Frequency || value != 0) return;
            Interlocked.Exchange(ref _pending, null)?
                .TrySetResult(Stopwatch.GetElapsedTime(_startedAt));
        }

        public string Host => inner.Host;

        public bool IsConnected => inner.IsConnected;

        public Task<bool> IsAvailableAsync(TimeSpan timeout, CancellationToken ct) =>
            inner.IsAvailableAsync(timeout, ct);

        public Task ConnectAsync(CancellationToken ct) => inner.ConnectAsync(ct);

        public Task DisconnectAsync(CancellationToken ct = default) => inner.DisconnectAsync(ct);

        public Task<ushort[]> ReadHoldingAsync(byte unit, ushort address, ushort count, string what,
            ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default) =>
            inner.ReadHoldingAsync(unit, address, count, what, priority, ct);

        public async Task WriteRegisterAsync(byte unit, ushort address, ushort value, string what,
            ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default)
        {
            await inner.WriteRegisterAsync(unit, address, value, what, priority, ct);
            NoteIfStopWrite(address, value);
        }

        public async Task WriteRegistersAsync(byte unit, ushort address, ushort[] values, string what,
            ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default)
        {
            await inner.WriteRegistersAsync(unit, address, values, what, priority, ct);
            if (values.Length > 0) NoteIfStopWrite(address, values[0]);
        }

        public Task<bool[]> ReadCoilsAsync(byte unit, ushort address, ushort count, string what,
            ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default) =>
            inner.ReadCoilsAsync(unit, address, count, what, priority, ct);

        public Task WriteCoilAsync(byte unit, ushort address, bool value, string what,
            ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default) =>
            inner.WriteCoilAsync(unit, address, value, what, priority, ct);

        public Task<bool[]> ReadDiscreteInputsAsync(byte unit, ushort address, ushort count, string what,
            ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default) =>
            inner.ReadDiscreteInputsAsync(unit, address, count, what, priority, ct);

        public void Dispose() => inner.Dispose();
    }
}
