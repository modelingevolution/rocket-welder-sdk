namespace RocketWelder.SDK.Devices.Motion;

/// <summary>
/// The kind of mechanism a motion device is — <b>derived</b> from its marker interface exactly as
/// <see cref="AxisKind"/> is derived from the typed axis leaf (FR-3). The marker is the primary
/// classification; this enum exists for display and telemetry.
///
/// <para>
/// <b>No kind without a marker.</b> A new member is additive only together with its
/// <c>IMotionDevice</c> marker interface — otherwise <c>IMotionDevice.Kind</c> could never return
/// it, and the enum would start describing devices the type system cannot resolve.
/// </para>
/// </summary>
public enum MotionDeviceKind
{
    /// <summary>A workpiece positioner — <see cref="IPositioner"/>.</summary>
    Positioner,

    /// <summary>A linear track carrying a robot or a workpiece — <see cref="ILinearTrack"/>.</summary>
    Track,
}
