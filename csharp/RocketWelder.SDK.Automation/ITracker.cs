using System.Numerics;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// Tracker interface, used to act as a LaserTracker or a CameraTracker device;
/// </summary>
public interface ITracker : IDisposable
{
    /// <summary>
    /// Correct that get's called by the tracker to update the correction vector for the robot's position.
    /// </summary>
    /// <param name="correction">X,Y position</param>
    void Correct(Vector<float> correction);
}
