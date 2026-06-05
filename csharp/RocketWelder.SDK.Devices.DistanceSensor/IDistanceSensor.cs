using RocketWelder.SDK.Abstractions;
namespace RocketWelder.SDK.Devices.DistanceSensor;

/// <summary>
/// Protocol-agnostic interface for a 1-D distance sensor.
///
/// The 1 Hz polling loop lives outside this interface — properties expose the
/// cached reading from the most recent successful poll. <see cref="TargetDistanceMM"/>,
/// <see cref="MinOffsetMM"/>, and <see cref="MaxOffsetMM"/> are immutable on this
/// interface: changes go through ConfigureDevice events, which cause the host to
/// rebuild the sensor via the device factory.
///
/// Implementations: <c>Adam6017DistanceSensor</c> (ADAM-6017 + Panasonic HL-G212B
/// over Modbus TCP).
/// </summary>
public interface IDistanceSensor : IDevice
{
    // ── Connection ────────────────────────────────────────────────

    /// <summary>Sensor endpoint. Modbus implementations use <c>modbus://host:port</c>.</summary>
    Uri Address { get; set; }

    /// <summary>True between a successful <see cref="ConnectAsync"/> and the next
    /// <see cref="Disconnect"/> or hard failure.</summary>
    bool IsConnected { get; }

    /// <summary>Cheap TCP probe — does NOT perform a Modbus transaction.</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>Establishes a Modbus session against <see cref="Address"/>.</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>Releases the Modbus session. Exceptions from the underlying client propagate.</summary>
    void Disconnect();

    /// <summary>Raised when a Modbus session has been successfully established.</summary>
    event EventHandler? Connected;

    /// <summary>Raised when the Modbus session has been torn down.</summary>
    event EventHandler? Disconnected;

    // ── Configured (immutable; sourced from ConfigSet at construction) ─

    /// <summary>
    /// The sensor's mid-range / optical-center / "sweetspot" distance (mm) — the distance
    /// at which the sensor reports zero offset. This is a SENSOR-HARDWARE property read
    /// from the device datasheet (e.g. 120 mm for the HL-G212B), NOT a welder-process
    /// setpoint chosen by the operator.
    /// </summary>
    double TargetDistanceMM { get; }

    /// <summary>
    /// Lower bound of the in-range window, expressed as a signed offset from
    /// <see cref="TargetDistanceMM"/> (mm). Defaults to the sensor's negative half-range
    /// (e.g. −30 for HL-G212B). May be narrowed by the operator for a stricter go/no-go
    /// classification (e.g. −5). Readings whose offset falls inside
    /// [<see cref="MinOffsetMM"/>, <see cref="MaxOffsetMM"/>] return
    /// <see cref="DistanceStatus.InRange"/>; otherwise <see cref="DistanceStatus.OutOfRange"/>.
    /// The aggregate enforces <c>MinOffsetMM ≤ 0 ≤ MaxOffsetMM</c> (target is always inside the window).
    /// </summary>
    double MinOffsetMM { get; }

    /// <summary>
    /// Upper bound of the in-range window, expressed as a signed offset from
    /// <see cref="TargetDistanceMM"/> (mm). See <see cref="MinOffsetMM"/> for full semantics.
    /// </summary>
    double MaxOffsetMM { get; }

    /// <summary>Convenience: <c>TargetDistanceMM + MinOffsetMM</c> (absolute lower bound).</summary>
    double MinDistanceMM => TargetDistanceMM + MinOffsetMM;

    /// <summary>Convenience: <c>TargetDistanceMM + MaxOffsetMM</c> (absolute upper bound).</summary>
    double MaxDistanceMM => TargetDistanceMM + MaxOffsetMM;

    // ── Cached reading (atomic snapshot) ──────────────────────────

    /// <summary>
    /// Most recent reading. Returns <see cref="DistanceReading.None"/> before the first
    /// successful read or after a connection drop that has not yet been recovered.
    /// </summary>
    DistanceReading LastReading { get; }

    /// <summary><c>true</c> and reading when <see cref="LastReading"/>.Status != <see cref="DistanceStatus.NoReading"/>.</summary>
    bool TryGetMeasurement(out DistanceReading reading);

    // ── Live I/O ──────────────────────────────────────────────────

    /// <summary>
    /// Self-healing pre-flight read. Does whatever is needed to produce a fresh reading:
    /// if the sensor is not currently connected, first calls <see cref="IsAvailableAsync"/>
    /// (cheap TCP probe) and <see cref="ConnectAsync"/>; then performs one Modbus transaction.
    /// On success: updates <see cref="LastReading"/> atomically and returns the new reading.
    /// On any failure (unreachable, connect refused, Modbus error): returns <c>null</c>
    /// and leaves <see cref="LastReading"/> unchanged.
    ///
    /// This is the "did the sensor just work?" question — a single non-null return means
    /// the sensor is reachable, connected, and just produced a fresh reading. Use it for
    /// pre-flight go/no-go checks where one call should answer
    /// "can I trust this sensor right now?".
    /// </summary>
    Task<DistanceReading?> ReadAsync(CancellationToken ct = default);

    // ── Sensor-bound services ─────────────────────────────────────

    /// <summary>
    /// Projector that lifts the sensor's 1-D reading to a 3D point in robot-base frame,
    /// composing the eye-in-hand offset (<c>sensorToTcp</c>) with the current TCP pose.
    /// Mirrors <c>RocketWelder.SDK.Vision.ICameraProjector</c>'s role for a 1-D sensor.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown by the getter (or by methods on the returned projector) when no
    /// <c>IRobot</c> is registered, since projection requires a TCP pose.
    /// </exception>
    IDistanceSensorProjector Projector { get; }

    /// <summary>
    /// Sensor-bound view locator. Computes TCP poses that aim the beam perpendicular to a
    /// target plane at the sensor's sweet-spot distance (<see cref="TargetDistanceMM"/>).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown by the getter (or by methods on the returned locator) when no
    /// <c>IRobot</c> is registered, since locator output is a TCP pose.
    /// </exception>
    IDistanceSensorViewLocator Locator { get; }
}
