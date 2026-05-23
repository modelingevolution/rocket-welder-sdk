using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Automation.AdaptivePoints;

/// <summary>
/// Computes the robot TCP pose that places the camera in a usable view of a target.
///
/// <para>
/// The interface is intentionally <b>method-generic</b> rather than interface-generic: a single
/// registration handles every kind of target via internal dispatch on the runtime type. Today
/// the only known caller passes a <see cref="Pose3{T}"/> (the operator-taught teach-point);
/// future targets such as <c>ViewTarget</c>, <c>AdaptivePoint</c>, or <c>PartModel</c> plug in
/// by extending the implementation's internal switch.
/// </para>
///
/// <para>
/// The returned pose is a <b>robot TCP pose</b>, not a camera pose — the implementation has
/// already applied the hand-eye conversion (<see cref="Vision.ICameraProjector.GetTcpForCameraPose"/>).
/// Drive the result directly with <see cref="IRobot.MoveLin"/>.
/// </para>
///
/// <para>
/// Implementations live outside the SDK (the SDK ships the contract only — concrete locators
/// depend on application-side conventions such as standoff distance and roll preference).
/// </para>
/// </summary>
public interface ICameraViewLocator
{
    /// <summary>
    /// Returns the TCP pose to move the robot to so that <paramref name="camera"/> views
    /// <paramref name="target"/>.
    /// </summary>
    /// <typeparam name="TTarget">Type of the target — the implementation dispatches internally.</typeparam>
    /// <param name="target">What to view (e.g. a teach-point <see cref="Pose3{T}"/>).</param>
    /// <param name="camera">The camera that will take the photo.</param>
    /// <returns>TCP pose ready to feed to <see cref="IRobot.MoveLin"/>.</returns>
    /// <exception cref="NotSupportedException">The implementation does not handle <typeparamref name="TTarget"/>.</exception>
    Pose3<double> FindTcpViewFor<TTarget>(TTarget target, ICamera camera);
}
