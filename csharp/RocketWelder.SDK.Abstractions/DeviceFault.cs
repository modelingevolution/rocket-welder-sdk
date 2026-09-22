namespace RocketWelder.SDK.Abstractions;

/// <summary>
/// One fault a device is currently reporting, in the device's own terms.
/// </summary>
/// <param name="Code">The vendor's fault code (a Megmeet <c>Err28</c> is 28, a Fronius <c>038</c> is 38, an
/// axis' <c>MotionError</c> is its enum value). 0 is never a fault.</param>
/// <param name="Text">What the code means, for the operator — the vendor's wording when the adapter knows it,
/// otherwise <c>"Err{Code}"</c>. Never empty.</param>
/// <param name="Since">When the adapter first saw this fault, so a row can say how long it has stood.</param>
/// <remarks>
/// A struct on purpose: <see cref="IFaultReporting.Fault"/> is read on the host's status poll, and a nullable
/// struct is one field read with no allocation per tick.
/// </remarks>
public readonly record struct DeviceFault(int Code, string Text, DateTimeOffset Since);
