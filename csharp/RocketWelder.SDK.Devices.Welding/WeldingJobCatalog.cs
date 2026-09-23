using RocketWelder.SDK.Abstractions;

namespace RocketWelder.SDK.Devices.Welding;

// epic-110 design.md §1 "SDK: RocketWelder.SDK.Devices.Welding 2.28.0". Additive over 2.27.0 (PL-2).
// No probe interface lives here (decisions.md D-9): probing is a member of the concrete TPS 5000 adapter.
// Hosts persist WeldingJobProbeOutcome and WeldingMode by ordinal: the member order is part of this contract, append only.

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
/// A RocketWelder-side snapshot of a manual mode's setpoints (FR-5.1). Never a welder job (D-5): it is applied by
/// writing the mode and setpoints, and is never stored on the welder.
/// </summary>
/// <param name="Id">Recipe identity; saving a recipe with an existing <paramref name="Id"/> replaces it (rename).</param>
/// <param name="Name">Operator-given name.</param>
/// <param name="Mode">Manual mode the setpoints belong to (Synergic / Standard).</param>
/// <param name="Setpoints">That mode's setpoints, keyed by setpoint name.</param>
/// <param name="LinkedJobNumber">Job number the recipe is linked to (FR-5.4); <c>null</c> = not linked.</param>
public sealed record WeldingRecipe(Guid Id, string Name, WeldingMode Mode, IReadOnlyDictionary<string, float> Setpoints,
    int? LinkedJobNumber);

/// <summary>
/// One job slot of the per-welder catalog (FR-3.1): host-side name and hidden flag, the last probe, the linked recipe.
/// </summary>
/// <param name="JobNumber">Welder job number; its group is <c>JobNumber / 10</c> (FR-3.2).</param>
/// <param name="Name">Host-side name (up to 20 characters, FR-3.3); <c>null</c> = unnamed.</param>
/// <param name="Hidden">True when the operator hid the job from the browser.</param>
/// <param name="LastProbe">Most recent probe of this job; <c>null</c> = never probed.</param>
/// <param name="LinkedRecipe">Recipe linked to this job (FR-5.4); <c>null</c> = none.</param>
public sealed record WeldingJobEntry(int JobNumber, string? Name, bool Hidden, WeldingJobProbeRecord? LastProbe,
    WeldingRecipe? LinkedRecipe);

/// <summary>
/// One group of ten jobs of the per-welder catalog (FR-3.1, FR-3.2): group <c>g</c> holds jobs <c>g*10 .. g*10+9</c>.
/// </summary>
/// <param name="GroupNumber">Group number, 0..99.</param>
/// <param name="Name">Host-side name (up to 20 characters, FR-3.3); <c>null</c> = unnamed.</param>
/// <param name="PopulatedCount">Jobs of the group whose last probe is <see cref="WeldingJobProbeOutcome.Populated"/>.</param>
/// <param name="ProbedCount">Jobs of the group that have been probed at least once.</param>
public sealed record WeldingJobGroup(int GroupNumber, string? Name, int PopulatedCount, int ProbedCount);

/// <summary>
/// Host service: the per-welder job catalog (FR-3, FR-5), keyed by the welder's <see cref="DeviceId"/>.
/// A host that does not register it gets the degraded panel (FR-7.1).
/// <para>
/// Validation failures throw <see cref="ArgumentException"/> / <see cref="ArgumentOutOfRangeException"/>;
/// <see cref="LinkRecipeAsync"/> with an unknown recipe id throws <see cref="InvalidOperationException"/>;
/// <see cref="DeleteRecipeAsync"/> with an unknown id is a no-op; event-store failures propagate unchanged.
/// </para>
/// </summary>
public interface IWeldingJobCatalog
{
    /// <summary>Raised after the read model applied a live event for the given welder.</summary>
    event Action<DeviceId>? Changed;

    /// <summary>The welder's groups: exactly 100, index = group number.</summary>
    IReadOnlyList<WeldingJobGroup> Groups(DeviceId welder);

    /// <summary>The jobs of one group: exactly 10, <c>JobNumber = group*10 + slot</c>.</summary>
    IReadOnlyList<WeldingJobEntry> Jobs(DeviceId welder, int group);

    /// <summary>One job of the welder's catalog.</summary>
    WeldingJobEntry Job(DeviceId welder, int jobNumber);

    /// <summary>The welder's recipes, ordered by <see cref="WeldingRecipe.Name"/>, ordinal-ignore-case.</summary>
    IReadOnlyList<WeldingRecipe> Recipes(DeviceId welder);

    /// <summary>Names a group (0..99); <c>null</c> clears the name.</summary>
    Task NameGroupAsync(DeviceId welder, int group, string? name, CancellationToken ct = default);

    /// <summary>Names a job; <c>null</c> clears the name.</summary>
    Task NameJobAsync(DeviceId welder, int jobNumber, string? name, CancellationToken ct = default);

    /// <summary>Hides or shows a job in the browser.</summary>
    Task HideJobAsync(DeviceId welder, int jobNumber, bool hidden, CancellationToken ct = default);

    /// <summary>Records probe results (one probe or a scan's batch).</summary>
    Task RecordProbesAsync(DeviceId welder, IReadOnlyList<WeldingJobProbeRecord> probes, CancellationToken ct = default);

    /// <summary>Saves a recipe; a recipe with the same <see cref="WeldingRecipe.Id"/> is replaced (rename).</summary>
    Task SaveRecipeAsync(DeviceId welder, WeldingRecipe recipe, CancellationToken ct = default);

    /// <summary>Links a recipe to a job (FR-5.4); <paramref name="jobNumber"/> <c>null</c> = unlink.</summary>
    Task LinkRecipeAsync(DeviceId welder, Guid recipeId, int? jobNumber, CancellationToken ct = default);

    /// <summary>Deletes a recipe and unlinks it from its job (FR-5.6, D-18).</summary>
    Task DeleteRecipeAsync(DeviceId welder, Guid recipeId, CancellationToken ct = default);
}
