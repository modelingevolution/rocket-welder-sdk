using ModelingEvolution.Signals;

namespace RocketWelder.SDK.Devices.Welding;

/// <summary>
/// A welding machine that can sense contact between the wire and the workpiece. Probed by cast, like
/// <c>IStoppableRobot</c> — never by device type name — so a host asks "can this welder touch-sense?" rather than
/// assuming a vendor.
///
/// <para><b>Why methods and not a pair of signals</b> (epic-107 ADR-2): writing a
/// <see cref="WritableSignal{T}"/> only latches a request. On the iWave it goes out one register per 100 ms tick in
/// a fixed priority order, and "a sample newer than this moment" is fieldbus-specific — poll-driven Modbus versus
/// event-driven CANopen PDOs. Only the adapter knows when a write landed, when a sample is newer than a moment, and
/// whether the machine is ready, so the adapter owns all three. A host that got either wrong would descend onto the
/// part with sensing off.</para>
///
/// <para><b>Armed</b> means: the welder reported ready on fresh data <i>and</i> the touch-sensing command was
/// written to it. The iWave Standard Image has no "touch sensing active" output to read back, so there is nothing
/// stronger to promise.</para>
///
/// <para><b>After <c>Dispose</c></b>: every outstanding waiter has been faulted with
/// <see cref="TouchSensingLostException"/> (reason <see cref="TouchSensingLostReason.DeviceChanged"/>) before the
/// adapter cancels and joins its I/O loop, and later calls throw the same — except
/// <see cref="DisarmTouchSensingAsync"/> and <see cref="RequestTouchSensingOff"/>, which return at once without
/// throwing so that a teardown path can call them unconditionally.</para>
/// </summary>
public interface ITouchSensingWelder : IWeldingMachine
{
    /// <summary>
    /// Switches touch sensing on. Records the arm <b>intent</b> first, before any wait: a
    /// <see cref="RequestTouchSensingOff"/>, a cancellation of <paramref name="ct"/>, the timeout or a refusal
    /// withdraws it, and a withdrawn intent never switches sensing on — an on-command already written or in flight
    /// is followed by an off-command at once, so touch sensing is never left on behind a caller that has given up.
    ///
    /// <para>Refuses unless machine data acquired <i>after this call began</i> shows the welder ready. Completes
    /// only when an on-command has <b>landed</b> while this intent is still current and the fast watch cadence is
    /// running; a landed off-command never completes it.</para>
    /// </summary>
    /// <param name="timeout">How long to wait for the on-command to land. Callers use 2 s.</param>
    /// <param name="ct">Cancels the arm and withdraws the intent.</param>
    /// <exception cref="TouchSensingNotReadyException">
    /// The welder is not ready; <see cref="TouchSensingNotReadyException.Reason"/> names the condition. Also thrown
    /// when the arc is on (welding start high), when another arm intent is already outstanding, or while
    /// <see cref="TouchSensingHoldOffRemaining"/> is running.
    /// </exception>
    /// <exception cref="TouchSignalStaleException">Machine data stopped arriving while the readiness read or the arm waiter ran.</exception>
    /// <exception cref="TouchSensingLostException">The device was disposed while arming.</exception>
    /// <exception cref="TimeoutException">The on-command did not land within <paramref name="timeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled, or the intent was withdrawn by <see cref="RequestTouchSensingOff"/>.</exception>
    Task ArmTouchSensingAsync(TimeSpan timeout, CancellationToken ct);

    /// <summary>
    /// Requests touch sensing off and waits for the off-command to land. <b>Bounded</b>: completes within 500 ms
    /// whether or not it landed, so a probe's <c>finally</c> can await it without ever hanging. On a missed write
    /// the adapter logs Critical and leaves the write pending, so the next flush still clears it.
    ///
    /// <para><b>Never throws</b> — not on a dead link, not after <c>Dispose</c> (where it returns at once).
    /// <paramref name="ct"/> may shorten the wait; it does not make this throw.</para>
    /// </summary>
    /// <param name="ct">Optional early exit from the wait. Teardown paths pass <see cref="CancellationToken.None"/>.</param>
    Task DisarmTouchSensingAsync(CancellationToken ct);

    /// <summary>
    /// The STOP channel. Withdraws any arm intent and latches the off-command whenever touch sensing could be on —
    /// that is, an on-command was sent and no off-command has landed since — and <b>returns at once</b>, never
    /// waiting for the machine. Writes nothing only when there is neither an intent nor a possible on-state.
    ///
    /// <para>Shaped like the welding-start-off latch beside it so that a STOP is never delayed by a fieldbus write:
    /// the arm is stopped without waiting for the welder. <b>Never throws</b>, including after <c>Dispose</c>,
    /// where it is a no-op.</para>
    /// </summary>
    void RequestTouchSensingOff();

    /// <summary>
    /// Contact state from machine data acquired <i>after this call began</i> — a fresh read, not the latched value
    /// of <see cref="TouchSignal"/>. Used to check that the signal is not stuck high before arming, that the wire
    /// is not already touching the part after arming, and that it has gone low again after the retract.
    /// </summary>
    /// <param name="timeout">How long to wait for a sample newer than this call. Callers use 1 s.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>True when the wire is in contact with the workpiece.</returns>
    /// <exception cref="TimeoutException">No fresh sample arrived within <paramref name="timeout"/>.</exception>
    /// <exception cref="TouchSignalStaleException">Machine data stopped arriving (see <see cref="LastSampleTimestamp"/>).</exception>
    /// <exception cref="TouchSensingLostException">The device was disposed while reading.</exception>
    Task<bool> ReadTouchAsync(TimeSpan timeout, CancellationToken ct);

    /// <summary>
    /// The fast watch: completes true as soon as contact is seen, false when <paramref name="timeout"/> elapses.
    /// Valid only while armed — the adapter samples at roughly 1 ms then, so a descent at 10 mm/s is watched every
    /// 0.01 mm.
    ///
    /// <para>Staleness is enforced by <b>this waiter's own timer</b>, whether or not the adapter's I/O loop is
    /// still running, so a link that goes silent without an error cannot leave the caller watching nothing: it
    /// faults after 100 ms without machine data while armed (the fast cadence is ~1 ms, so 100x margin), or after
    /// 300 ms before the fast cadence starts, where the machine's own cadence applies — 100 ms on the iWave, which
    /// a single 100 ms limit would have left no margin against.</para>
    /// </summary>
    /// <param name="timeout">How long to watch. Pass <see cref="Timeout.InfiniteTimeSpan"/> to watch until the move ends.</param>
    /// <param name="ct">Cancels the watch.</param>
    /// <returns>True on contact; false when <paramref name="timeout"/> elapsed with no contact.</returns>
    /// <exception cref="TouchSignalStaleException">No machine data within the limit for the cadence that is running.</exception>
    /// <exception cref="TouchSensingLostException">
    /// The armed command was found cleared (<see cref="TouchSensingLostReason.SwitchedOff"/> — another fieldbus
    /// master), or the device was disposed (<see cref="TouchSensingLostReason.DeviceChanged"/>).
    /// </exception>
    /// <exception cref="TouchSensingNotReadyException">The welder stopped reporting ready.</exception>
    Task<bool> WaitForTouchAsync(TimeSpan timeout, CancellationToken ct);

    /// <summary>
    /// <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> of the most recent <b>successful</b> read of machine
    /// status; 0 before the first. Lock-free (a <c>Volatile</c> read), never blocks and never throws, so a caller's
    /// own watchdog can read it independently of every waiter — a value that stops advancing means the link or the
    /// adapter is dead, whatever the waiters do, and 0 counts as stale.
    ///
    /// <para>What counts as stale depends on the cadence that is running: roughly 1 ms while armed, the machine's
    /// own period otherwise (100 ms on the iWave), which is why the limits are two-tier — 100 ms armed, 300 ms
    /// before arming.</para>
    /// </summary>
    long LastSampleTimestamp { get; }

    /// <summary>
    /// Time left before the machine accepts the next <b>rising</b> command after touch sensing that <i>could have
    /// been</i> on went off — on Fronius, 4 s counted from the landed off-command. Off-commands are never deferred.
    ///
    /// <para><see cref="Timeout.InfiniteTimeSpan"/> while that off-command is still pending: a caller must not
    /// proceed before it lands. <see cref="TimeSpan.Zero"/> when no hold-off is running, including after a run that
    /// never switched touch sensing on.</para>
    /// </summary>
    TimeSpan TouchSensingHoldOffRemaining { get; }

    /// <summary>
    /// Live contact state for the UI and the signal catalog. <b>No freshness guarantee</b> — it is the latched value
    /// of the last poll, so a probe must use <see cref="ReadTouchAsync"/> or <see cref="WaitForTouchAsync"/>
    /// instead. Same <see cref="ISignal{T}"/> semantics as the rest of <see cref="IWeldingMachine"/>: gate on
    /// <see cref="ISignal{T}.HasValue"/> before reading <see cref="ISignal{T}.Value"/>.
    /// </summary>
    ISignal<bool> TouchSignal { get; }
}
