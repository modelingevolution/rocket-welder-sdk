using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Devices.DistanceSensor;

/// <summary>
/// Lifts a 1-D distance reading to a 3D point in robot-base frame, composing the
/// sensor's eye-in-hand offset (<c>sensorToTcp</c>) with the current TCP pose and
/// the sensor's most recent reading.
///
/// <para>
/// Mirrors <c>RocketWelder.SDK.Vision.ICameraProjector</c>'s role for a 1-D sensor: holds the
/// eye-in-hand calibration internally and returns 3D coordinates in robot-base frame.
/// Bound to its owning <see cref="IDistanceSensor"/> at construction; consumers obtain
/// the instance via <see cref="IDistanceSensor.Projector"/>.
/// </para>
/// </summary>
public interface IDistanceSensorProjector
{
    /// <summary>
    /// 3D point where the sensor beam currently terminates, in robot-base frame.
    /// Composes the sensor's <see cref="IDistanceSensor.LastReading"/>, the current
    /// TCP pose, and the <c>sensorToTcp</c> offset.
    /// </summary>
    /// <returns>
    /// The 3D point, or <c>null</c> if the sensor has no fresh reading.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// No <c>IRobot</c> is registered, so there is no TCP pose to compose with.
    /// </exception>
    Point3<double>? GetPoint();
}
