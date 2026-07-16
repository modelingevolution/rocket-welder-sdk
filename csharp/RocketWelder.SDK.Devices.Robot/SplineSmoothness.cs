namespace RocketWelder.SDK.Devices.Robot;

/// <summary>
/// Controller-agnostic smoothness for a spline move. Each robot driver maps it to its own
/// parameters (e.g. Fairino → NewSplineStart averageTime + NewSplinePoint blend radius);
/// drivers that do not support tuning ignore it. Higher = smoother/rounder path through the
/// waypoints; lower = tighter to the points.
/// </summary>
public enum SplineSmoothness { Low, Medium, High }
