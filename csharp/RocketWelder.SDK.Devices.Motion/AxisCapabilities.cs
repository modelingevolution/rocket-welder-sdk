namespace RocketWelder.SDK.Devices.Motion;

/// <summary>
/// What an axis can actually do (FR-4) — so a caller can <i>ask</i> instead of assume, and get a
/// truthful "no". Querying capabilities never causes motion (AC-18).
/// </summary>
[Flags]
public enum AxisCapabilities
{
    /// <summary>No optional capability.</summary>
    None = 0,

    /// <summary>
    /// The axis has a homing sequence. An absolute move on an unhomed axis that requires homing is
    /// rejected with <see cref="MotionError.NotHomed"/>.
    /// </summary>
    Homing = 1,

    /// <summary>
    /// The axis wraps: <c>Min</c>/<c>Max</c> describe the wrap domain [0°, 360°) rather than travel
    /// limits, absolute targets are normalised into it, and <see cref="RotationSense"/> selects the
    /// path. Without this flag any sense other than <see cref="RotationSense.Shortest"/> is rejected
    /// with <see cref="MotionError.UnsupportedSense"/>.
    /// </summary>
    ContinuousRotation = 2,

    /// <summary>
    /// The axis can join a coordinated (interpolated) motion group. <b>Every implementation
    /// delivered by epic-065 reports this false</b> — the flag exists so the seam is named, not
    /// silently degraded (AC-18).
    /// </summary>
    Synchronised = 4,
}
