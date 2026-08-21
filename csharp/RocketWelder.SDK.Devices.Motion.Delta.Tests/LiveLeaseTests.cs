using Microsoft.Extensions.Logging.Abstractions;

namespace RocketWelder.SDK.Devices.Motion.Delta.Tests;

/// <summary>
/// AC-12 against a live drive: a second controller instance started while a foreign heartbeat is
/// live <b>refuses to attach, names the owner it saw, and attaches on retry after the first instance
/// dies</b>.
///
/// <para>
/// This is the case risk R-5 is about — a cloned config pointing a second station at this drive, or
/// a deploy overlap. The lease is advisory (Modbus has no compare-and-swap) and the watchdog is what
/// bounds the consequence; what the lease buys is that the ordinary cases do not happen at all.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
[Collection(LiveSimulatorCollection.Name)]
public class LiveLeaseTests
{
    private static readonly TimeSpan Beat = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan Expiry = TimeSpan.FromSeconds(1);

    [SimFact]
    public async Task ASecondInstanceRefusesWhileTheFirstIsBeating_AndAttachesOnceItStops()
    {
        await LiveSimulator.QuiesceAsync(LiveSimulator.TurntablePort);

        // First instance: takes the lease and beats.
        var first = LiveSimulator.Observe(LiveSimulator.TurntablePort);
        await first.ConnectAsync(CancellationToken.None);
        var firstBeat = new DeltaHeartbeat("turntable", first, 301, Beat, Expiry, NullLogger.Instance);
        (await firstBeat.TryAcquireAsync(CancellationToken.None)).Granted.Should().BeTrue();
        firstBeat.Start();
        await Task.Delay(500);

        // Second instance: sees a live foreign heartbeat and declines.
        using var second = LiveSimulator.Observe(LiveSimulator.TurntablePort);
        await second.ConnectAsync(CancellationToken.None);
        await using var secondBeat = new DeltaHeartbeat("turntable", second, 302, Beat, Expiry,
            NullLogger.Instance);

        var refusal = await secondBeat.TryAcquireAsync(CancellationToken.None);
        refusal.Granted.Should().BeFalse();
        refusal.Reason.Should().Contain("301", "the refusal must name the owner it saw");

        // The first instance dies. Its socket goes; it never releases the lease.
        first.Dispose();

        // The successor attaches on retry — this is what makes a rolling deploy work.
        var attached = await LiveSimulator.WaitUntilAsync(
            async () => (await secondBeat.TryAcquireAsync(CancellationToken.None)).Granted,
            TimeSpan.FromSeconds(15));

        attached.Should().BeTrue();

        using var observer = LiveSimulator.Observe(LiveSimulator.TurntablePort);
        await observer.ConnectAsync(CancellationToken.None);
        (await LiveSimulator.ReadAsync(observer, DeltaRegisters.PlcUnit, DeltaRegisters.D131_OwnerId))
            .Should().Be(302);

        await firstBeat.DisposeAsync();
    }

    [SimFact]
    public async Task ConnectingAPositionerWithAForeignLeaseLive_FailsWithLeaseHeld()
    {
        // The same refusal, surfaced where a host actually meets it: ConnectAsync, before anything
        // is written that could move the machine.
        await LiveSimulator.QuiesceAsync(LiveSimulator.TurntablePort);

        var incumbent = LiveSimulator.Observe(LiveSimulator.TurntablePort);
        await incumbent.ConnectAsync(CancellationToken.None);
        var incumbentBeat = new DeltaHeartbeat("turntable", incumbent, 311, Beat, Expiry,
            NullLogger.Instance);
        await incumbentBeat.AcquireAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        incumbentBeat.Start();
        await Task.Delay(500);

        using var positioner = new DeltaPositioner(
            new Abstractions.DeviceId("DeltaPositioner_VFDC2000", Guid.NewGuid()),
            [LiveSimulator.Turntable], ownerId: 312, new InMemoryAxisStateStore(),
            leaseTimeout: TimeSpan.FromMilliseconds(1), NullLogger<DeltaPositioner>.Instance);

        var act = () => positioner.ConnectAsync();

        var ex = (await act.Should().ThrowAsync<MotionException>()).Which;
        ex.Error.Should().Be(MotionError.LeaseHeld);
        ex.Message.Should().Contain("311");

        incumbent.Dispose();
        await incumbentBeat.DisposeAsync();
    }

    [SimFact]
    public async Task ReattachingToOurOwnLeaseCostsNoWait()
    {
        // Rows 6 and 7 of the vector table, live: an instance that already owns the register does not
        // pay the sampling window to come back — which is what makes a reconnect after a transport
        // blip cheap.
        await LiveSimulator.QuiesceAsync(LiveSimulator.TurntablePort);

        using var channel = LiveSimulator.Observe(LiveSimulator.TurntablePort);
        await channel.ConnectAsync(CancellationToken.None);
        await using var heartbeat = new DeltaHeartbeat("turntable", channel, 321, Beat, Expiry,
            NullLogger.Instance);

        (await heartbeat.TryAcquireAsync(CancellationToken.None)).Granted.Should().BeTrue();

        var started = DateTime.UtcNow;
        var again = await heartbeat.TryAcquireAsync(CancellationToken.None);

        again.Granted.Should().BeTrue();
        again.Reason.Should().Contain("already ours");
        (DateTime.UtcNow - started).Should().BeLessThan(Expiry,
            "reattaching to our own lease must not wait out the expiry window");
    }

    [SimFact]
    public async Task AGracefulStopReleasesTheLease_SoTheNextInstanceAttachesImmediately()
    {
        await LiveSimulator.QuiesceAsync(LiveSimulator.TurntablePort);

        using var channel = LiveSimulator.Observe(LiveSimulator.TurntablePort);
        await channel.ConnectAsync(CancellationToken.None);
        var heartbeat = new DeltaHeartbeat("turntable", channel, 331, Beat, Expiry, NullLogger.Instance);
        await heartbeat.AcquireAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        heartbeat.Start();
        await Task.Delay(400);

        await heartbeat.StopAsync();

        using var successor = LiveSimulator.Observe(LiveSimulator.TurntablePort);
        await successor.ConnectAsync(CancellationToken.None);
        await using var successorBeat = new DeltaHeartbeat("turntable", successor, 332, Beat, Expiry,
            NullLogger.Instance);

        var started = DateTime.UtcNow;
        (await successorBeat.TryAcquireAsync(CancellationToken.None)).Granted.Should().BeTrue();

        (DateTime.UtcNow - started).Should().BeLessThan(Expiry,
            "a released lease reads as unowned, which costs no sampling wait at all");

        await heartbeat.DisposeAsync();
    }
}
