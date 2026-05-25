namespace RocketWelder.SDK.Http.Devices;

/// <summary>
/// Public identity of a registered welder device — name (optional), the
/// interface type it was registered against (as a CLR full name string for
/// JSON portability), and a coarse connection flag.
/// </summary>
/// <param name="Name">
/// Display name as registered. May be null for the default/unnamed instance.
/// </param>
/// <param name="Interface">
/// CLR full name of the device interface (e.g. <c>RocketWelder.SDK.Automation.IRobot</c>).
/// String, not <see cref="Type"/>, so it survives wire serialisation.
/// </param>
/// <param name="IsConnected">
/// Best-effort connection state. <c>false</c> for devices without a usable
/// <c>IsConnected</c> property or whose connection is down.
/// </param>
public sealed record DeviceInfo(
    string? Name,
    string Interface,
    bool IsConnected);
