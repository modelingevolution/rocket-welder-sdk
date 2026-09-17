namespace RocketWelder.SDK.Devices.Welding;

/// <summary>
/// The welder's machine data stopped arriving, so the touch signal can no longer be trusted. Thrown by
/// <see cref="ITouchSensingWelder.WaitForTouchAsync"/>, <see cref="ITouchSensingWelder.ReadTouchAsync"/> and the
/// waiter inside <see cref="ITouchSensingWelder.ArmTouchSensingAsync"/> when
/// <see cref="ITouchSensingWelder.LastSampleTimestamp"/> has not advanced within the limit for the cadence that is
/// actually running: while armed the adapter samples at roughly 1 ms and the limit is 100 ms; before the fast
/// cadence starts the machine's own period applies (the iWave polls at 100 ms) and the limit is 300 ms.
///
/// <para>This is a safety failure, not a timeout: a probe that sees it must stop the arm, because a descent is no
/// longer being watched. It is raised by each waiter's own timer, so it is thrown whether or not the adapter's I/O
/// loop is still running — a link that goes silent without an error still faults the waiters.</para>
/// </summary>
public class TouchSignalStaleException : TouchSensingException
{
    private const string DefaultMessage = "The welder's touch signal is not updating.";

    /// <summary>Creates the exception with the default message.</summary>
    public TouchSignalStaleException() : base(DefaultMessage) { }

    /// <summary>Creates the exception with <paramref name="message"/>.</summary>
    public TouchSignalStaleException(string message) : base(message) { }

    /// <summary>Creates the exception with <paramref name="message"/> and a cause.</summary>
    public TouchSignalStaleException(string message, Exception innerException) : base(message, innerException) { }
}
