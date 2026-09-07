namespace RocketWelder.SDK.Devices.Robot;

/// <summary>
/// A robot whose in-flight motion can be halted the instant a running program is cancelled. Deliberately kept
/// off <see cref="IRobot"/>: not every arm can be halted, and a host must be able to ask "can this one?" rather
/// than assume it. <see cref="IRobot"/> moves are blocking and take no cancellation token, so cancelling a run's
/// token on its own stops nothing — a taught move-only program walks through every remaining move. This seam
/// lets a host arm per-run cancellation: <see cref="BeginRun"/> halts the arm the moment the token is cancelled
/// and makes every subsequent blocking move throw <see cref="OperationCanceledException"/> instead of commanding
/// new motion.
///
/// <para>Published so a plugin can reach a robot's stop channel without referencing the host: a plugin that
/// latches a physical output (an arc relay, a clamp) resolves the bound robot through
/// <c>IDeviceResolver.Get&lt;IStoppableRobot&gt;</c> and drives the emergency path through
/// <see cref="Stop"/> when its own transport is lost.</para>
/// </summary>
public interface IStoppableRobot
{
    /// <summary>Immediately halts any in-flight motion via the controller's stop channel.</summary>
    void Stop();

    /// <summary>
    /// Arms per-run cancellation. While the returned scope is alive, cancelling <paramref name="ct"/> calls
    /// <see cref="Stop"/> at once, and any move issued after cancellation throws
    /// <see cref="OperationCanceledException"/> rather than starting a new motion. Dispose to end the run.
    /// </summary>
    IDisposable BeginRun(CancellationToken ct);
}
