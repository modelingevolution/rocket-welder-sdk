namespace RocketWelder.SDK.Devices.Motion;

/// <summary>
/// One axis of a motion device: identity, the explicit state machine, and lifecycle — and
/// <b>nothing unit-bearing</b>.
///
/// <para>
/// The base is deliberately unit-free so that <see cref="IMotionDevice.Axes"/> can hold a rotary
/// and a linear axis in one mechanism (a column-and-boom does exactly that) and every device-level
/// consumer — <c>HomeAllAsync</c>, the Devices page, the status service — works without knowing
/// which is which. Positions, speeds and speed bounds live on the typed leaves
/// <see cref="IRotaryAxis"/> and <see cref="ILinearAxis"/>, a closed set mirroring
/// revolute / prismatic. A dimensionally wrong position or speed therefore <b>does not compile</b>
/// (FR-2, AC-21).
/// </para>
///
/// <para>
/// Method names follow PLCopen Motion Control: <c>MC_Power</c>, <c>MC_Home</c>, <c>MC_Stop</c>,
/// <c>MC_Reset</c>. The <c>Execute</c>/<c>Done</c>/<c>Busy</c> handshake is deliberately <i>not</i>
/// copied — it is a scan-cycle idiom; an async <see cref="Task"/> that completes on arrival and
/// throws <see cref="MotionException"/> on failure says the same thing. <c>MC_Halt</c> is absent:
/// <see cref="StopAsync"/> covers both intents.
/// </para>
/// </summary>
public interface IMotionAxis
{
    /// <summary>
    /// The plugin-frozen identifier (FR-8) — role-based and vendor-neutral (<c>tilt</c>,
    /// <c>turntable</c>; never <c>delta-a</c>). Weld programs, automation programs and the devices
    /// hub all speak it, so it is frozen from first write and never editable from a station.
    /// </summary>
    string Name { get; }

    /// <summary>The human label. This is where renaming happens — <see cref="Name"/> never moves.</summary>
    string DisplayName { get; }

    /// <summary>The single explicit state (FR-1). Every convenience boolean derives from it.</summary>
    AxisState State { get; }

    /// <summary>What this axis can do (FR-4), so a caller can ask instead of assume.</summary>
    AxisCapabilities Capabilities { get; }

    /// <summary>The most recent reading, without going to the wire.</summary>
    AxisStatus Status { get; }

    /// <summary>Reads the axis's live status from the device.</summary>
    Task<AxisStatus> ReadStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Raised when the status changes. On an idle axis the cadence comes from the FR-11 heartbeat's
    /// paired read, so an unmoving axis still reports.
    /// </summary>
    event EventHandler<AxisStatus>? StatusChanged;

    /// <summary>Powers the axis on or off (<c>MC_Power</c>).</summary>
    Task PowerAsync(bool on, CancellationToken ct = default);

    /// <summary>Runs the homing sequence (<c>MC_Home</c>).</summary>
    Task HomeAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops the axis (<c>MC_Stop</c>). Commanded within 200 ms of the call (NFR-5) — it takes a
    /// priority lane on the transport, so a long move or a homing hold cannot delay it.
    /// </summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>Clears a latched fault (<c>MC_Reset</c>) — the only exit from
    /// <see cref="AxisState.ErrorStop"/>.</summary>
    Task ResetAsync(CancellationToken ct = default);

    /// <summary>
    /// The axis kind, <b>derived</b> from the typed leaf this axis implements. There is exactly ONE
    /// classification mechanism: the leaf interface is the truth and this enum is computed from it,
    /// so an implementation cannot declare a kind that contradicts its own type.
    /// </summary>
    /// <exception cref="NotSupportedException">The implementation extends
    /// <see cref="IMotionAxis"/> without implementing a typed leaf — it is outside the contract's
    /// closed set and has no unit.</exception>
    AxisKind Kind => this switch
    {
        IRotaryAxis => AxisKind.Rotary,
        ILinearAxis => AxisKind.Linear,
        _ => throw new NotSupportedException($"{GetType().Name} implements no typed axis leaf"),
    };
}
