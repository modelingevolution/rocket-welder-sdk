namespace RocketWelder.SDK.Devices.Welding;

/// <summary>
/// A welding machine whose operator trims — setpoints and corrections — can be adjusted from a host surface (the
/// preview rail on a touch kiosk). Probed by cast, like <see cref="ITouchSensingWelder"/> — never by device type
/// name — so a host asks "what can the operator trim on this welder right now?" rather than assuming a vendor.
///
/// <para>The set depends on the welder's <b>confirmed</b> mode, and the adapter owns that decision: on a Fronius
/// TPS 5000, Job offers power and arc-length correction, Synergic adds dynamics, Standard offers wire-feed speed,
/// welding voltage and dynamics. A host re-reads <see cref="Adjustments"/> whenever
/// <see cref="IWeldingMachine.ModeSignal"/> changes; it never filters or reorders the list.</para>
/// </summary>
public interface IAdjustableWelder : IWeldingMachine
{
    /// <summary>
    /// The adjustments valid in the welder's confirmed mode, in the order the welder's own panel shows them. Empty
    /// when the mode is unknown or offers none. Cheap: a cached list per mode, safe to read on every render.
    /// </summary>
    IReadOnlyList<IWeldingAdjustment> Adjustments { get; }
}

/// <summary>What an <see cref="IWeldingAdjustment"/> trims. A host localises its label from this.</summary>
public enum WeldingAdjustmentKind
{
    /// <summary>Power / characteristic position (synergic and job modes), %.</summary>
    Power = 0,
    /// <summary>Arc-length correction (synergic and job modes), %.</summary>
    ArcLengthCorrection = 1,
    /// <summary>Dynamics / arc-force correction, %.</summary>
    Dynamics = 2,
    /// <summary>Wire-feed speed setpoint (standard mode), m/min.</summary>
    WireFeedSpeed = 3,
    /// <summary>Welding voltage setpoint (standard mode), V.</summary>
    WeldingVoltage = 4,
}

/// <summary>
/// One operator trim, in <b>display units</b> (the adapter converts to its typed signal). Stepped only — a touch
/// surface offers one <see cref="Step"/> down or up per tap, never free entry — so a live arc moves one step at a
/// time. <see cref="Set"/> clamps to [<see cref="Min"/>, <see cref="Max"/>] and latches a write that goes out on
/// the adapter's next tick; <see cref="IsSyncing"/> stays true until the welder's read-back confirms it, and a value
/// the welder never reflects reverts to the welder's truth. All members are cheap, non-blocking reads a UI may poll.
/// </summary>
public interface IWeldingAdjustment
{
    /// <summary>What this trims.</summary>
    WeldingAdjustmentKind Kind { get; }

    /// <summary>Display unit, e.g. <c>%</c>, <c>m/min</c>, <c>V</c>.</summary>
    string Unit { get; }

    /// <summary>.NET numeric format for <see cref="Value"/>, e.g. <c>0</c> or <c>0.0</c>.</summary>
    string Format { get; }

    /// <summary>Lowest accepted value (may come from runtime limits read off the welder).</summary>
    float Min { get; }

    /// <summary>Highest accepted value.</summary>
    float Max { get; }

    /// <summary>One tap's change.</summary>
    float Step { get; }

    /// <summary>False until the welder reported a value.</summary>
    bool HasValue { get; }

    /// <summary>The current value in display units; meaningful only while <see cref="HasValue"/>.</summary>
    float Value { get; }

    /// <summary>True while a written value awaits the welder's read-back.</summary>
    bool IsSyncing { get; }

    /// <summary>Requests <paramref name="value"/>, clamped to the range. Never blocks on the fieldbus.</summary>
    void Set(float value);
}
