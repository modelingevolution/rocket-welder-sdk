using Microsoft.Extensions.Logging.Abstractions;
using RocketWelder.SDK.Abstractions;

namespace RocketWelder.SDK.Devices.Motion.Delta.Tests;

/// <summary>
/// A known gap in FR-11's arming semantics, pinned so it is a fact rather than a suspicion.
/// See <c>dev-log.md</c> §"Open questions for the reviewer" Q2.
///
/// <para>
/// The ladder arms on the first change of D130 and disarms only <b>after a trip</b>. It has no notion
/// of a commander leaving on purpose. So a clean <c>DisconnectAsync</c> — beat stopped, lease
/// released, axis already at rest — leaves the network armed, and one stall window later it latches
/// a watchdog fault on a drive nobody is doing anything to. The next attach then reports
/// <see cref="MotionError.WatchdogTripped"/> and demands a reset: "the fault everyone learns to
/// ignore", which the arming rule was written to prevent at power-up, reappearing at shutdown.
/// </para>
///
/// <para>
/// <b>These tests describe today's behaviour, not desired behaviour.</b> When the AC-25 ladder edit
/// disarms on <c>D131 → 0</c> — which a graceful shutdown already writes, so it costs one rung and no
/// new register — they will start failing, and that failure is the signal to delete them. The
/// adapter cannot close this from its side: clearing D132 on attach would swallow a real trip a
/// human should see.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
[Collection(LiveSimulatorCollection.Name)]
public class LiveCleanShutdownTests
{
    [SimFact]
    public async Task ACleanDisconnectStillLeavesTheWatchdogArmed_AndItLatchesASpuriousTrip()
    {
        await LiveSimulator.QuiesceAsync(LiveSimulator.TurntablePort);
        using var observer = LiveSimulator.Observe(LiveSimulator.TurntablePort);
        await observer.ConnectAsync(CancellationToken.None);

        using (var positioner = new DeltaPositioner(
                   new DeviceId("DeltaPositioner_VFDC2000", Guid.NewGuid()), [LiveSimulator.Turntable],
                   ownerId: 401, new InMemoryAxisStateStore(), TimeSpan.FromSeconds(10),
                   NullLogger<DeltaPositioner>.Instance))
        {
            await positioner.ConnectAsync();

            (await LiveSimulator.WaitUntilArmedAsync(observer, TimeSpan.FromSeconds(10)))
                .Should().BeTrue();

            // A deliberate, orderly shutdown: the beat stops and the lease is released.
            await positioner.DisconnectAsync();
        }

        (await LiveSimulator.ReadAsync(observer, DeltaRegisters.PlcUnit, DeltaRegisters.D131_OwnerId))
            .Should().Be(AdvisoryLease.Unowned, "a graceful shutdown releases the lease");

        var latched = await LiveSimulator.WaitUntilAsync(async () =>
            await LiveSimulator.ReadAsync(observer, DeltaRegisters.PlcUnit,
                DeltaRegisters.D132_WatchdogFault) == DeltaRegisters.WatchdogHeartbeatStall,
            TimeSpan.FromSeconds(10));

        latched.Should().BeTrue(
            "TODAY the ladder cannot tell a commander that left from one that died, so it faults a "
            + "drive nobody is touching. If this assertion starts failing, the AC-25 ladder edit has "
            + "closed the gap and this whole class should go");

        // And this is the cost: a perfectly healthy restart is met with a fault to be reset.
        await LiveSimulator.QuiesceAsync(LiveSimulator.TurntablePort);
    }
}
