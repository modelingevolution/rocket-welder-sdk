namespace RocketWelder.SDK.Devices.Welding;

/// <summary>
/// Base for every failure of the <see cref="ITouchSensingWelder"/> capability. Published so a caller that only
/// needs "touch sensing failed" — a probe's <c>finally</c>, a teardown path — can catch one type, while the
/// probe algorithm still distinguishes <see cref="TouchSensingNotReadyException"/>,
/// <see cref="TouchSignalStaleException"/> and <see cref="TouchSensingLostException"/> to pick the operator text.
/// </summary>
public class TouchSensingException : Exception
{
    /// <summary>Creates the exception with the runtime's default message.</summary>
    public TouchSensingException() { }

    /// <summary>Creates the exception with <paramref name="message"/>.</summary>
    public TouchSensingException(string message) : base(message) { }

    /// <summary>Creates the exception with <paramref name="message"/> and a cause.</summary>
    public TouchSensingException(string message, Exception innerException) : base(message, innerException) { }
}
