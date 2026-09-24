using RocketWelder.SDK.Abstractions;

namespace RocketWelder.SDK.Devices.Welding;

// epic-110 design.md §1 "SDK: RocketWelder.SDK.Devices.Welding 2.28.0". Additive over 2.27.0 (PL-2).
// No probe interface lives here (decisions.md D-9): probing is a member of the concrete TPS 5000 adapter.
// Hosts persist WeldingJobProbeOutcome and WeldingMode by ordinal: the member order is part of this contract, append only.
// Recipes were removed before release (decisions.md D-22, 2026-09-24).

/// <summary>
/// Result of probing one welder job: select it, read the operating point back, classify (FR-4.3).
/// Persisted by ordinal; append only.
/// </summary>
public enum WeldingJobProbeOutcome
{
    /// <summary>The welder accepted the job and reported an operating point: the job slot holds a stored job.</summary>
    Populated = 0,

    /// <summary>The welder reported the job slot as empty (no stored job under that number).</summary>
    Empty = 1,

    /// <summary>The job was selected but its content could not be read back or classified.</summary>
    Unreadable = 2,
}

/// <summary>
/// Operating point the welder reported for a selected job; <c>null</c> = that register could not be read.
/// Register sources (TPS 5000 Modbus process image): <see cref="CurrentA"/> E028 0.1 A/LSB,
/// <see cref="VoltageV"/> E027 0.01 V/LSB, <see cref="WireFeedMMin"/> E00B 0.01 m/min/LSB,
/// <see cref="SheetThicknessMm"/> E029 0.01 mm/LSB, <see cref="Program"/> F00A.
/// </summary>
/// <param name="CurrentA">Welding current, amperes.</param>
/// <param name="VoltageV">Welding voltage, volts.</param>
/// <param name="WireFeedMMin">Wire feed speed, metres per minute.</param>
/// <param name="SheetThicknessMm">Sheet thickness, millimetres.</param>
/// <param name="Program">Welder's synergic program (characteristic) number.</param>
public readonly record struct WeldingJobSnapshot(float? CurrentA, float? VoltageV, float? WireFeedMMin,
    float? SheetThicknessMm, int? Program);

/// <summary>
/// One probe result for one welder job, as recorded into the host catalog (FR-4.2, FR-4.4).
/// </summary>
/// <param name="JobNumber">Welder job number, 0..999.</param>
/// <param name="Outcome">Classification of the probe.</param>
/// <param name="Snapshot">Operating point read back while the job was selected.</param>
/// <param name="ProbedAt">When the probe read back.</param>
public sealed record WeldingJobProbeRecord(int JobNumber, WeldingJobProbeOutcome Outcome, WeldingJobSnapshot Snapshot,
    DateTimeOffset ProbedAt);

/// <summary>
/// One job slot of the per-welder catalog (FR-3.1): host-side name and hidden flag, the last probe.
/// </summary>
/// <param name="JobNumber">Welder job number; its group is <c>JobNumber / 10</c> (FR-3.2).</param>
/// <param name="Name">Host-side name (up to 20 characters, FR-3.3); <c>null</c> = unnamed.</param>
/// <param name="Hidden">True when the operator hid the job from the browser.</param>
/// <param name="LastProbe">Most recent probe of this job; <c>null</c> = never probed.</param>
public sealed record WeldingJobEntry(int JobNumber, string? Name, bool Hidden, WeldingJobProbeRecord? LastProbe);

/// <summary>
/// One group of ten jobs of the per-welder catalog (FR-3.1, FR-3.2): group <c>g</c> holds jobs <c>g*10 .. g*10+9</c>.
/// </summary>
/// <param name="GroupNumber">Group number, 0..99.</param>
/// <param name="Name">Host-side name (up to 20 characters, FR-3.3); <c>null</c> = unnamed.</param>
/// <param name="PopulatedCount">Jobs of the group whose last probe is <see cref="WeldingJobProbeOutcome.Populated"/>.</param>
/// <param name="ProbedCount">Jobs of the group that have been probed at least once.</param>
public sealed record WeldingJobGroup(int GroupNumber, string? Name, int PopulatedCount, int ProbedCount);

/// <summary>
/// Host service: the per-welder job catalog (FR-3), keyed by the welder's <see cref="DeviceId"/>.
/// A host that does not register it gets the degraded panel (FR-7.1).
/// <para>
/// Validation failures throw <see cref="ArgumentException"/> / <see cref="ArgumentOutOfRangeException"/>;
/// event-store failures propagate unchanged.
/// </para>
/// </summary>
/// <remarks>
/// Behaviour every implementation (the rw2 event-sourced store, an in-memory test catalog) must share — epic-110
/// design §1/§2:
/// <list type="bullet">
/// <item><b>Ranges</b>: groups 0..99, jobs 0..999. <see cref="Jobs"/>, <see cref="Job"/> and every write method throw
/// <see cref="ArgumentOutOfRangeException"/> outside them.</item>
/// <item><b>Names</b> (groups and jobs, FR-3.3): trimmed; <c>null</c>, empty or blank after trimming = unnamed
/// (clears the name); longer than 20 characters after trimming → <see cref="ArgumentException"/>. Writing the current
/// value (name or hidden flag) records nothing.</item>
/// <item><b>Probes</b>: <see cref="RecordProbesAsync"/> with an empty list records nothing.</item>
/// <item><b>Eventually consistent reads</b>: after a write completes, <see cref="Groups"/>, <see cref="Jobs"/>,
/// and <see cref="Job"/> may still return the previous value until <see cref="Changed"/> fires
/// for that welder. Re-read on <see cref="Changed"/>; do not assume read-your-write.</item>
/// </list>
/// </remarks>
public interface IWeldingJobCatalog
{
    /// <summary>
    /// Raised for a welder after the catalog applied a change for it, and ALSO once per known welder when the host's
    /// read model catches up on start (a view built during catch-up may be incomplete).
    /// <para>
    /// Raised on a background thread (the host's event subscription), never on a UI thread: marshal with
    /// <c>InvokeAsync</c>, never block in the handler (a blocking handler can delay notifications for every welder),
    /// and unsubscribe on dispose — the catalog is a singleton and keeps a forgotten subscriber alive.
    /// </para>
    /// </summary>
    event Action<DeviceId>? Changed;

    /// <summary>The welder's groups: exactly 100, index = group number.</summary>
    IReadOnlyList<WeldingJobGroup> Groups(DeviceId welder);

    /// <summary>The jobs of one group: exactly 10, <c>JobNumber = group*10 + slot</c>.</summary>
    IReadOnlyList<WeldingJobEntry> Jobs(DeviceId welder, int group);

    /// <summary>One job of the welder's catalog.</summary>
    WeldingJobEntry Job(DeviceId welder, int jobNumber);

    /// <summary>Names a group (0..99); <c>null</c> or blank clears the name; trimmed, at most 20 characters.</summary>
    Task NameGroupAsync(DeviceId welder, int group, string? name, CancellationToken ct = default);

    /// <summary>Names a job (0..999); <c>null</c> or blank clears the name; trimmed, at most 20 characters.</summary>
    Task NameJobAsync(DeviceId welder, int jobNumber, string? name, CancellationToken ct = default);

    /// <summary>Hides or shows a job in the browser.</summary>
    Task HideJobAsync(DeviceId welder, int jobNumber, bool hidden, CancellationToken ct = default);

    /// <summary>Records probe results (one probe or a scan's batch); an empty list records nothing.</summary>
    Task RecordProbesAsync(DeviceId welder, IReadOnlyList<WeldingJobProbeRecord> probes, CancellationToken ct = default);
}
