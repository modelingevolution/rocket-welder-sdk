namespace RocketWelder.SDK.Devices.Welding;

/// <summary>
/// Touch sensing stopped being armed under a caller that was relying on it. Thrown by
/// <see cref="ITouchSensingWelder.WaitForTouchAsync"/> when the armed command is found cleared
/// (<see cref="TouchSensingLostReason.SwitchedOff"/>), and by every outstanding waiter — arm, watch, fresh read —
/// when the device is disposed (<see cref="TouchSensingLostReason.DeviceChanged"/>), which the adapter does
/// <i>before</i> it cancels and joins its I/O loop. After disposal every member throws this exception except
/// <see cref="ITouchSensingWelder.DisarmTouchSensingAsync"/> and
/// <see cref="ITouchSensingWelder.RequestTouchSensingOff"/>, which stay no-throw.
///
/// <para><see cref="Reason"/> is the discriminator, not the message: a host picks its operator wording from it and
/// so never depends on text written in another package.</para>
/// </summary>
public class TouchSensingLostException : TouchSensingException
{
    private const string DefaultMessage = "Touch sensing is no longer armed.";

    /// <summary>Creates the exception for <see cref="TouchSensingLostReason.SwitchedOff"/> with the default message.</summary>
    public TouchSensingLostException() : this(TouchSensingLostReason.SwitchedOff, DefaultMessage) { }

    /// <summary>Creates the exception for <see cref="TouchSensingLostReason.SwitchedOff"/> with <paramref name="message"/>.</summary>
    public TouchSensingLostException(string message) : this(TouchSensingLostReason.SwitchedOff, message) { }

    /// <summary>Creates the exception for <see cref="TouchSensingLostReason.SwitchedOff"/> with <paramref name="message"/> and a cause.</summary>
    public TouchSensingLostException(string message, Exception innerException)
        : base(message, innerException) => Reason = TouchSensingLostReason.SwitchedOff;

    /// <summary>Creates the exception for <paramref name="reason"/> with the default message.</summary>
    /// <param name="reason">Which of the two losses this is.</param>
    public TouchSensingLostException(TouchSensingLostReason reason) : this(reason, DefaultMessage) { }

    /// <summary>Creates the exception for <paramref name="reason"/> with <paramref name="message"/>.</summary>
    /// <param name="reason">Which of the two losses this is.</param>
    /// <param name="message">Developer-facing description; operator text is chosen from <paramref name="reason"/>.</param>
    public TouchSensingLostException(TouchSensingLostReason reason, string message) : base(message) => Reason = reason;

    /// <summary>Why touch sensing was lost. Defaults to <see cref="TouchSensingLostReason.SwitchedOff"/>.</summary>
    public TouchSensingLostReason Reason { get; }
}
