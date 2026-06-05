using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Vision;

/// <summary>
/// Camera-bound view locator: computes the robot TCP pose that puts the owning camera in
/// a usable view of a target. Obtained via <c>ICamera.Locator</c>.
///
/// <para>
/// The interface is intentionally <b>method-generic</b>: a single implementation handles every
/// kind of target via internal dispatch on the runtime type. Today the only known caller passes
/// a <see cref="Pose3{T}"/> (the operator-taught teach-point); future targets such as
/// <c>ViewTarget</c>, <c>AdaptivePoint</c>, or <c>PartModel</c> plug in by extending the
/// implementation's internal switch.
/// </para>
///
/// <para>
/// The returned pose is a <b>robot TCP pose</b>, not a camera pose — the implementation has
/// already applied the hand-eye conversion (<see cref="ICameraProjector.GetTcpForCameraPose"/>).
/// Drive the result directly with <c>IRobot.MoveLin</c>.
/// </para>
///
/// <para>
/// Implementations live outside the SDK and are wired per camera at construction; the SDK
/// ships the contract only.
/// </para>
/// </summary>
public interface ICameraViewLocator
{
    /// <summary>
    /// Returns the TCP pose to move the robot to so that the owning camera views
    /// <paramref name="target"/>.
    /// </summary>
    /// <typeparam name="TTarget">Type of the target — the implementation dispatches internally.</typeparam>
    /// <param name="target">What to view (e.g. a teach-point <see cref="Pose3{T}"/>).</param>
    /// <returns>TCP pose ready to feed to <c>IRobot.MoveLin</c>.</returns>
    /// <exception cref="NotSupportedException">The implementation does not handle <typeparamref name="TTarget"/>.</exception>
    /// <exception cref="InvalidOperationException">No <c>IRobot</c> is registered.</exception>
    Pose3<double> FindTcpViewFor<TTarget>(TTarget target);
}
