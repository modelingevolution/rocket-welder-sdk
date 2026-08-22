using Microsoft.Extensions.Time.Testing;

namespace RocketWelder.SDK.Devices.Motion.Delta.Tests;

/// <summary>
/// The channel's two exceptions to first-come-first-served: NFR-5's stop lane, which preempts queued
/// move traffic so a 26 s homing hold cannot make 200 ms impossible (AC-23), and FR-11's
/// <b>bounded</b> heartbeat deferral, so a long move cannot starve the beat into a self-trip (AC-24).
///
/// <para>
/// Driven by a fake clock, so what is being asserted is the ordering rule and not the machine this
/// test happens to run on.
/// </para>
/// </summary>
public class PriorityGateTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task AStopPreemptsMoveTrafficAlreadyQueuedAheadOfIt()
    {
        var clock = new FakeTimeProvider();
        using var gate = new PriorityGate(clock, Bound);

        var held = await gate.AcquireAsync(ChannelPriority.Move, default);

        var firstMove = gate.AcquireAsync(ChannelPriority.Move, default).AsTask();
        var secondMove = gate.AcquireAsync(ChannelPriority.Move, default).AsTask();
        var stop = gate.AcquireAsync(ChannelPriority.Stop, default).AsTask();

        firstMove.IsCompleted.Should().BeFalse();
        held.Dispose();

        var granted = await Task.WhenAny(stop, firstMove, secondMove).WaitAsync(TimeSpan.FromSeconds(5));
        granted.Should().BeSameAs(stop, "the stop lane jumps the two moves that queued before it");
        (await stop).Dispose();
    }

    [Fact]
    public async Task TheHeartbeatYieldsToTheMoveLoop_WhileItsWaitIsStillShort()
    {
        var clock = new FakeTimeProvider();
        using var gate = new PriorityGate(clock, Bound);

        var held = await gate.AcquireAsync(ChannelPriority.Move, default);
        var beat = gate.AcquireAsync(ChannelPriority.Heartbeat, default).AsTask();
        var move = gate.AcquireAsync(ChannelPriority.Move, default).AsTask();

        clock.Advance(TimeSpan.FromMilliseconds(50));
        held.Dispose();

        var granted = await Task.WhenAny(move, beat).WaitAsync(TimeSpan.FromSeconds(5));
        granted.Should().BeSameAs(move, "50 ms is well inside the deferral bound");
        (await move).Dispose();
        (await beat.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [Fact]
    public async Task TheHeartbeatTakesTheNextSlotOnceItsWaitReachesTheBound()
    {
        // The bound is what turns "deprioritised" into "deferred by at most 200 ms". Without it a
        // long move holds the channel indefinitely and the commander trips its own watchdog.
        var clock = new FakeTimeProvider();
        using var gate = new PriorityGate(clock, Bound);

        var held = await gate.AcquireAsync(ChannelPriority.Move, default);
        var beat = gate.AcquireAsync(ChannelPriority.Heartbeat, default).AsTask();
        var move = gate.AcquireAsync(ChannelPriority.Move, default).AsTask();

        clock.Advance(Bound);
        held.Dispose();

        var granted = await Task.WhenAny(beat, move).WaitAsync(TimeSpan.FromSeconds(5));
        granted.Should().BeSameAs(beat, "at the bound the beat takes its reserved slot");
        (await beat).Dispose();
        (await move.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [Fact]
    public async Task StopStillOutranksAHeartbeatThatHasWaitedPastTheBound()
    {
        var clock = new FakeTimeProvider();
        using var gate = new PriorityGate(clock, Bound);

        var held = await gate.AcquireAsync(ChannelPriority.Move, default);
        var beat = gate.AcquireAsync(ChannelPriority.Heartbeat, default).AsTask();
        clock.Advance(TimeSpan.FromSeconds(5));
        var stop = gate.AcquireAsync(ChannelPriority.Stop, default).AsTask();

        held.Dispose();

        var granted = await Task.WhenAny(stop, beat).WaitAsync(TimeSpan.FromSeconds(5));
        granted.Should().BeSameAs(stop, "nothing outranks a stop");
        (await stop).Dispose();
        (await beat.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [Fact]
    public async Task WithinALaneTheOrderIsFirstComeFirstServed()
    {
        var clock = new FakeTimeProvider();
        using var gate = new PriorityGate(clock, Bound);
        var order = new List<int>();

        var held = await gate.AcquireAsync(ChannelPriority.Move, default);
        var waiters = Enumerable.Range(0, 3).Select(async i =>
        {
            using var _ = await gate.AcquireAsync(ChannelPriority.Move, default);
            lock (order) order.Add(i);
            await Task.Delay(1);
        }).ToArray();

        // Let all three reach the queue before releasing, so the order under test is the queue's.
        await Task.Delay(50);
        held.Dispose();
        await Task.WhenAll(waiters).WaitAsync(TimeSpan.FromSeconds(5));

        order.Should().Equal(0, 1, 2);
    }

    [Fact]
    public async Task ACancelledWaiterIsSkippedRatherThanLeakingTheChannel()
    {
        var clock = new FakeTimeProvider();
        using var gate = new PriorityGate(clock, Bound);
        using var cts = new CancellationTokenSource();

        var held = await gate.AcquireAsync(ChannelPriority.Move, default);
        var abandoned = gate.AcquireAsync(ChannelPriority.Move, cts.Token).AsTask();
        var survivor = gate.AcquireAsync(ChannelPriority.Move, default).AsTask();

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);

        held.Dispose();

        (await survivor.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [Fact]
    public async Task Control_AnUncontendedAcquireCompletesImmediately()
    {
        // Without this, every "X was granted first" assertion could be passing because the gate
        // grants nothing and the test is reading a never-completed task.
        var clock = new FakeTimeProvider();
        using var gate = new PriorityGate(clock, Bound);

        var task = gate.AcquireAsync(ChannelPriority.Move, default);

        task.IsCompleted.Should().BeTrue();
        (await task).Dispose();
    }
}
