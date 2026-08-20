namespace RocketWelder.SDK.Devices.Motion;

/// <summary>
/// Which hardware travel limits are currently active.
/// <para>
/// <see cref="Min"/> and <see cref="Max"/> asserted together is a <b>wiring fault</b>, and is
/// reported as such rather than masked to one side or to <see cref="None"/> — the whole point of
/// the flags shape is that the impossible reading stays visible.
/// </para>
/// </summary>
[Flags]
public enum LimitSwitchState
{
    /// <summary>Neither limit is active.</summary>
    None = 0,

    /// <summary>The lower travel limit is active.</summary>
    Min = 1,

    /// <summary>The upper travel limit is active.</summary>
    Max = 2,
}
