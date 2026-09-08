namespace RocketWelder.SDK.Devices.Robot;

/// <summary>
/// The weave <b>instruction family</b> — which start/end method pair a driver issues when a weave is turned on
/// and off. Each family has a start variant (configure + turn weaving on) and an end variant (turn weaving off).
/// A Fairino driver maps these onto <c>WeaveStart/WeaveEnd</c>, <c>WeaveStartSim/WeaveEndSim</c>,
/// <c>WeaveInspectStart/WeaveInspectEnd</c>, and — for <see cref="FixedPoint"/> — <c>WeaveStart/WeaveEnd</c> with
/// <c>weaveStationary = 1</c>. Fairino's fifth family (swing gradient, a two-profile amplitude ramp) is out of
/// scope for this epic and has no member here.
/// </summary>
public enum WeaveInstruction
{
    /// <summary>Ordinary weaving. Fairino: <c>WeaveStart</c> / <c>WeaveEnd</c>.</summary>
    Swinging,

    /// <summary>Swing simulation (dry run, no arc). Fairino: <c>WeaveStartSim</c> / <c>WeaveEndSim</c>.</summary>
    Simulation,

    /// <summary>Trajectory warning (inspection). Fairino: <c>WeaveInspectStart</c> / <c>WeaveInspectEnd</c>.</summary>
    TrajectoryWarning,

    /// <summary>
    /// Fixed-point oscillation: the torch oscillates about a stationary point rather than travelling along the
    /// path. Fairino: <c>WeaveStart</c> / <c>WeaveEnd</c> with <c>weaveStationary = 1</c>.
    /// </summary>
    FixedPoint
}
