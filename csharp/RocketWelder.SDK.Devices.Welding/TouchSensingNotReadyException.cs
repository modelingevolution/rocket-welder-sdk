namespace RocketWelder.SDK.Devices.Welding;

/// <summary>
/// The welder refused to switch touch sensing on, or stopped reporting ready while it was on. Thrown by
/// <see cref="ITouchSensingWelder.ArmTouchSensingAsync"/> when machine data acquired after the call began does not
/// show the welder ready, and by <see cref="ITouchSensingWelder.WaitForTouchAsync"/> when readiness is lost during
/// the watch — in which case the caller must stop the arm, because contact can no longer be detected.
///
/// <para><see cref="Reason"/> is the vendor's own wording for the condition that failed (on the Fronius iWave:
/// power source not ready, system not ready, a non-zero error number, safety status Halt or Stop, collision box
/// inactive, process active, or Robot ready low). A host composes its operator text around it rather than
/// reformatting <see cref="Exception.Message"/>.</para>
/// </summary>
public class TouchSensingNotReadyException : TouchSensingException
{
    /// <summary>Creates the exception with the runtime's default message and an empty <see cref="Reason"/>.</summary>
    public TouchSensingNotReadyException() => Reason = string.Empty;

    /// <summary>Creates the exception whose message and <see cref="Reason"/> are both <paramref name="reason"/>.</summary>
    /// <param name="reason">Vendor wording for the condition that failed, e.g. <c>"Robot ready is low"</c>.</param>
    public TouchSensingNotReadyException(string reason) : base(reason) => Reason = reason;

    /// <summary>Creates the exception with <paramref name="reason"/> and a cause.</summary>
    /// <param name="reason">Vendor wording for the condition that failed.</param>
    /// <param name="innerException">The underlying transport or protocol failure.</param>
    public TouchSensingNotReadyException(string reason, Exception innerException)
        : base(reason, innerException) => Reason = reason;

    /// <summary>
    /// Vendor wording for the condition that failed; never null, empty only when the adapter gave none. A host
    /// interpolates it into its own message ("The welder is not ready for touch sensing: {Reason}.").
    /// </summary>
    public string Reason { get; }
}
