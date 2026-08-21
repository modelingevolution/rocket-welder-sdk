namespace RocketWelder.SDK.Devices.Motion.Delta;

/// <summary>
/// FR-11's advisory lease — <b>the epic's reference implementation</b>.
///
/// <para>
/// Production owns the rule. <c>AdvisoryLease.Evaluate</c> in <c>delta-positioner-sim</c> was the
/// executable specification until this landed and is the <b>mirrored test oracle</b> now. The two
/// are kept in agreement by a shared table of lease test vectors — <b>shared test data, not a
/// shared package</b>: a production adapter must not depend on a simulator library, and the
/// simulator repository publishes none. The table lives in
/// <c>delta-positioner-sim/docs/register-map.md</c> and is copied verbatim into
/// <c>AdvisoryLeaseTests</c> on both sides. Changing this method's behaviour is therefore a
/// cross-repository contract change: change the table first, then both implementations.
/// </para>
///
/// <para>
/// <b>Advisory, stated plainly.</b> Modbus has no compare-and-swap, so two instances starting inside
/// the same window can both pass this check (vector row 8). The watchdog bounds the consequence; no
/// CAS ladder logic is added, because that would grow the unversioned-ladder artefact (risk R-1) to
/// close a seconds-wide race.
/// </para>
///
/// <para>
/// <b>Evaluation is client-side by necessity.</b> The drive owes only <see cref="LeaseOwnerRegister"/>
/// (D131) and an observable heartbeat (D130); it enforces nothing. An instance that sees a fresh
/// foreign heartbeat declines to attach and retries at <see cref="RetryInterval"/> until expiry —
/// which is what lets a rolling deploy attach as soon as the outgoing instance stops beating.
/// </para>
/// </summary>
public static class AdvisoryLease
{
    /// <summary>Value of D131 meaning nobody holds the lease.</summary>
    public const ushort Unowned = 0;

    /// <summary>The register the rule reads the owner id from — <c>D131</c>.</summary>
    public const ushort LeaseOwnerRegister = DeltaRegisters.D131_OwnerId;

    /// <summary>
    /// Decides whether this instance may attach to a drive.
    ///
    /// <para>
    /// The rule in one line: <b>grant if the register is unowned, or it is already ours, or the
    /// incumbent's heartbeat has aged past expiry.</b> The comparison is <c>age &gt;= expiry</c>, not
    /// <c>&gt;</c> — a heartbeat exactly at the window is <i>already</i> expired (vector row 4). That
    /// single line is the one an independent implementation is most likely to get subtly wrong.
    /// </para>
    /// </summary>
    /// <param name="ownerRegister">Value read from <c>D131</c>.</param>
    /// <param name="heartbeatAge">How long <c>D130</c> has been unchanged. Not readable from any
    /// register — the drive publishes the heartbeat <i>value</i>, not its age — so an attaching
    /// instance obtains it by sampling D130 across the expiry window: unchanged means at least
    /// <paramref name="expiry"/> old, changed means fresh. Skipping that sampling wait and reading a
    /// stale value once is the mistake vector row 2 exists to catch.</param>
    /// <param name="expiry">Lease expiry — the same window as the watchdog's stall timeout (1 s).</param>
    /// <param name="myOwnerId">This instance's station-unique 16-bit id (Delta D registers are
    /// 16-bit).</param>
    /// <returns>The decision, and why — worth logging on both outcomes.</returns>
    public static LeaseDecision Evaluate(ushort ownerRegister, TimeSpan heartbeatAge, TimeSpan expiry,
        ushort myOwnerId)
    {
        if (ownerRegister == Unowned)
            return new LeaseDecision(true, $"D131 is {Unowned} — unowned");

        if (ownerRegister == myOwnerId)
            return new LeaseDecision(true, $"D131 is {myOwnerId} — already ours, reattaching");

        if (heartbeatAge >= expiry)
            return new LeaseDecision(true,
                $"D131 is {ownerRegister} but its heartbeat is {heartbeatAge.TotalSeconds:F2} s old "
                + $"(expiry {expiry.TotalSeconds:F2} s) — the lease has expired");

        return new LeaseDecision(false,
            $"D131 is {ownerRegister} and its heartbeat is {heartbeatAge.TotalSeconds:F2} s old — "
            + $"another instance holds the drive; retry at 1 Hz until {expiry.TotalSeconds:F2} s");
    }

    /// <summary>The interval a refused instance waits before trying again. FR-11 pins it at 1 Hz.</summary>
    public static TimeSpan RetryInterval { get; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Lease expiry, equal to the watchdog's stall window (FR-11 decision D-f: 5 Hz beat, 1 s stall).
    /// By the time a foreign lease is takeable the drive has already been stopped by its own
    /// watchdog, which is why taking it is safe rather than merely permitted.
    /// </summary>
    public static TimeSpan Expiry { get; } = TimeSpan.FromSeconds(1);
}

/// <summary>Outcome of a lease check.</summary>
/// <param name="Granted">The instance may attach.</param>
/// <param name="Reason">Why — worth logging on both outcomes, and the text AC-12 reads the refused
/// owner's id out of.</param>
public readonly record struct LeaseDecision(bool Granted, string Reason);
