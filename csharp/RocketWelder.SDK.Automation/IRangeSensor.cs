using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// Tool-mounted laser range sensor. Anchors Z and auto-probes the reference surface
/// for adaptive-point capture. Minimal contract this epic depends on; the full
/// device-management lifecycle (registration, calibration UX) lives in a parallel
/// device epic.
/// </summary>
public interface IRangeSensor : IDisposable
{
    /// <summary>True when the sensor is connected and producing readings.</summary>
    bool IsConnected { get; }

    /// <summary>Eye-in-hand offset — the sensor's pose relative to the gripper TCP.</summary>
    Pose3<double> RangeToGripper { get; }

    /// <summary>
    /// Most recent range in mm along the sensor's ray, or null if no valid return
    /// (out of focus, blocked, beyond bounds).
    /// </summary>
    double? ReadRange();
}
