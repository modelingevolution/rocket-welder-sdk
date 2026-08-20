using ModelingEvolution.Drawing;
using RocketWelder.SDK.Abstractions;

namespace RocketWelder.SDK.Devices.Positioner;

/// <summary>
/// Vendor-agnostic interface for a welding positioner — a table carrying the workpiece on one or
/// more independent rotary axes (typically a tilt and a turntable).
///
/// <para>
/// A positioner is NOT a robot. Its axes have no shared kinematics, no TCP and no pose: each one is
/// an angle that can be commanded independently. Modelling it as an <c>IRobot</c> would leave most
/// of that contract throwing, so it gets its own family.
/// </para>
///
/// <para>
/// <b>Motion is asynchronous but the API is not.</b> Underlying protocols usually accept a command
/// and finish in the background; every method here completes only when the motion has finished, so
/// callers never poll. Cancellation stops the axis.
/// </para>
///
/// <para>Implementations: <c>DeltaPositioner</c> (Delta VFD-C2000 over Modbus TCP).</para>
/// </summary>
public interface IPositioner : IDevice
{
    // ── Connection ────────────────────────────────────────────────

    /// <summary>Positioner endpoint. Modbus implementations use <c>modbus://host</c>; multi-drive
    /// positioners take the per-axis addresses from configuration instead.</summary>
    Uri Address { get; set; }

    /// <summary>True between a successful <see cref="ConnectAsync"/> and the next
    /// <see cref="Disconnect"/> or hard failure.</summary>
    bool IsConnected { get; }

    /// <summary>Cheap reachability probe — does NOT perform a protocol transaction.</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>Establishes sessions to every axis drive.</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>Releases all sessions. Does not move anything.</summary>
    void Disconnect();

    /// <summary>Raised when all axis sessions are established.</summary>
    event EventHandler? Connected;

    /// <summary>Raised when the sessions have been torn down or lost.</summary>
    event EventHandler? Disconnected;

    // ── Axes ──────────────────────────────────────────────────────

    /// <summary>The axes, in declaration order.</summary>
    IReadOnlyList<IPositionerAxis> Axes { get; }

    /// <summary>Axis by name (case-insensitive).</summary>
    /// <exception cref="KeyNotFoundException">No axis with that name.</exception>
    IPositionerAxis this[string name] { get; }

    /// <summary>Tries to get an axis by name.</summary>
    bool TryGetAxis(string name, out IPositionerAxis axis);

    // ── Whole-positioner operations ───────────────────────────────

    /// <summary>
    /// Homes every axis that requires it, concurrently — the axes are mechanically independent.
    /// Completes when all have finished; the first failure is thrown after the rest settle.
    /// </summary>
    Task HomeAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Ramps every axis to a stop. This is the interlock path a supervising program calls on fault
    /// or abort, so it returns as soon as the stop is COMMANDED, without waiting for deceleration.
    /// <para>
    /// This is not an emergency stop: it depends on the network, this process and the drive all
    /// being alive. An E-stop must be hardware.
    /// </para>
    /// </summary>
    Task StopAllAsync(CancellationToken ct = default);

    /// <summary>True when every axis is ready to accept a motion command.</summary>
    bool IsReady { get; }
}

/// <summary>
/// One independently commandable axis of a <see cref="IPositioner"/>.
/// </summary>
public interface IPositionerAxis
{
    /// <summary>Stable identifier used in configuration and API calls (e.g. <c>"tilt"</c>).</summary>
    string Name { get; }

    /// <summary>Human-readable label for UI.</summary>
    string DisplayName { get; }

    // ── Capabilities (immutable) ──────────────────────────────────

    /// <summary>Lower travel limit.</summary>
    Degree<double> Min { get; }

    /// <summary>Upper travel limit.</summary>
    Degree<double> Max { get; }

    /// <summary>
    /// True for an endless rotary axis: angles wrap at 360°, <see cref="MoveToAsync"/> takes the
    /// short way round, and <see cref="RotateAsync"/> is available.
    /// </summary>
    bool IsContinuous { get; }

    /// <summary>True when absolute positioning requires a completed <see cref="HomeAsync"/>.</summary>
    bool RequiresHoming { get; }

    /// <summary>Slowest speed the axis reliably turns at. Commands below this are rejected rather
    /// than silently raised — a caller must be able to learn its request was not honoured.</summary>
    double MinSpeedDegPerSecond { get; }

    /// <summary>Fastest speed permitted for this axis.</summary>
    double MaxSpeedDegPerSecond { get; }

    /// <summary>Positioning tolerance: a move completes once inside this band.</summary>
    Degree<double> Tolerance { get; }

    // ── Live state ────────────────────────────────────────────────

    /// <summary>Most recent complete status snapshot.</summary>
    PositionerAxisStatus Status { get; }

    /// <summary>Reads a fresh status snapshot from the drive.</summary>
    Task<PositionerAxisStatus> ReadStatusAsync(CancellationToken ct = default);

    /// <summary>Raised after every status refresh, whether polled or read on demand.</summary>
    event EventHandler<PositionerAxisStatus>? StatusChanged;

    // ── Motion ────────────────────────────────────────────────────

    /// <summary>
    /// Runs the homing procedure and completes when the zero has been captured.
    /// <para>
    /// The axis does NOT return to 0° afterwards — it stops where the search left it, and the angle
    /// is simply recounted from the new zero.
    /// </para>
    /// </summary>
    /// <exception cref="PositionerException">Homing failed; the axis is left stopped and unhomed.</exception>
    Task HomeAsync(CancellationToken ct = default);

    /// <summary>
    /// Moves to an absolute angle and completes when the axis is inside <see cref="Tolerance"/>.
    /// A continuous axis takes the shorter direction.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Target outside <see cref="Min"/>..<see cref="Max"/>.</exception>
    /// <exception cref="PositionerException">Not homed, limit tripped, stalled, or tolerance not reached.</exception>
    Task MoveToAsync(Degree<double> target, CancellationToken ct = default);

    /// <summary>
    /// Turns continuously until stopped. Only for <see cref="IsContinuous"/> axes.
    /// <para>
    /// Returns as soon as motion is commanded. Position keeps being tracked, so the angle stays
    /// valid after stopping.
    /// </para>
    /// </summary>
    /// <exception cref="NotSupportedException">Axis does not support continuous rotation.</exception>
    Task RotateAsync(double speedDegPerSecond, RotationDirection direction, CancellationToken ct = default);

    /// <summary>Ramps the axis to a stop and cancels any operation in flight.</summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>Energises or de-energises the drive. Motion commands do this themselves.</summary>
    Task SetServoAsync(bool on, CancellationToken ct = default);

    /// <summary>Sets the traverse speed used by <see cref="MoveToAsync"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Outside <see cref="MinSpeedDegPerSecond"/>..<see cref="MaxSpeedDegPerSecond"/>.
    /// </exception>
    Task SetSpeedAsync(double degPerSecond, CancellationToken ct = default);

    /// <summary>Clears a drive fault and the last operation error, then re-energises.</summary>
    Task ResetFaultAsync(CancellationToken ct = default);

    /// <summary>
    /// Verifies that the drive's wiring matches the controller's internal convention — that
    /// commanding the positive direction actually increases the position count.
    /// <para>
    /// Worth calling at commissioning: a positioner wired the other way round drives the correct
    /// distance the WRONG way, ending up opposite the target — which does not look like a wiring
    /// fault, it looks like a broken control loop. This moves the axis a few tenths of a degree.
    /// </para>
    /// </summary>
    /// <returns>True when the wiring matches the convention.</returns>
    Task<bool> VerifyDirectionAsync(CancellationToken ct = default);
}

/// <summary>Direction of continuous rotation.</summary>
public enum RotationDirection
{
    /// <summary>Direction that increases the reported angle.</summary>
    Forward,

    /// <summary>Direction that decreases the reported angle.</summary>
    Reverse,
}
