using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Devices.Robot;

/// <summary>
/// A robot whose actual TCP pose can be read <b>while a move is still running</b>. Deliberately kept off
/// <see cref="IRobot"/> and probed by cast, like <see cref="IStoppableRobot"/>: not every arm can answer a pose
/// query mid-motion, and a host must be able to ask rather than assume.
///
/// <para><b>Why a capability and not a change to <see cref="IRobot.TryGetActualPose"/></b> (epic-107 M-4): on the
/// Fairino the existing read waits for motion to end, and 57 call sites — hand-eye, capture, triangulation, the
/// arc-off lead watcher — may rely on exactly that. Flipping it globally would change all of them at once. So
/// <see cref="IRobot.GetActualPose"/> and <see cref="IRobot.TryGetActualPose"/> keep their "after the motion
/// ended" behaviour and this seam adds the non-blocking read beside them.</para>
///
/// <para>Needed by anything that has to watch the arm during its own move: a touch probe reads Z at standstill
/// right after a stop, and samples it every few milliseconds to decide that the arm has actually stopped.</para>
/// </summary>
public interface INonBlockingPoseRobot : IRobot
{
    /// <summary>
    /// Reads the current TCP pose in the robot base frame without waiting for any in-flight motion to end.
    /// Implementations must return promptly (single-digit milliseconds) and must never block on the move.
    /// </summary>
    /// <param name="pose">The pose read, when this returns true; otherwise undefined and not to be used.</param>
    /// <returns>
    /// False when the read failed or returned nothing — <b>never</b> true with a zero or identity pose standing in
    /// for a failed read, because a caller comparing Z would read that as the arm having moved.
    /// </returns>
    bool TryGetActualPoseNow(out Pose3<double> pose);
}
