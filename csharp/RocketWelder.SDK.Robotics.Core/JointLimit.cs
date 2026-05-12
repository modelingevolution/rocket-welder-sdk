namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// Joint angle limits in degrees.
/// </summary>
public readonly record struct JointLimit(double MinDeg, double MaxDeg)
{
    /// <summary>
    /// Returns true if the given angle (degrees) is within limits (inclusive).
    /// </summary>
    public bool Contains(double angleDeg) => angleDeg >= MinDeg && angleDeg <= MaxDeg;

    /// <summary>
    /// Returns the overshoot amount in degrees (0 if within limits).
    /// Positive means above max, negative means below min.
    /// </summary>
    public double Overshoot(double angleDeg)
    {
        if (angleDeg > MaxDeg) return angleDeg - MaxDeg;
        if (angleDeg < MinDeg) return angleDeg - MinDeg;
        return 0;
    }
}
