namespace RocketWelder.SDK.Devices.Motion;

/// <summary>
/// String-keyed accessors that bind a name to a typed leaf.
///
/// <para>
/// <b>For generated code and generic consumers only</b> (FR-10). These appear inside
/// <c>Program.g.cs</c> and in code that iterates a roster it cannot know at compile time — never in
/// a human-written automation program, which uses the generated facade
/// (<c>Positioner.Tilt.MoveAbsoluteAsync(…)</c>) so that a typo in an axis name is a compile error
/// rather than the runtime failure these methods must raise.
/// </para>
/// </summary>
public static class MotionDeviceExtensions
{
    /// <summary>Binds a declared name to its rotary leaf.</summary>
    /// <param name="d">The device holding the axis.</param>
    /// <param name="name">The plugin-frozen axis name.</param>
    /// <exception cref="MotionException">No axis of that name is declared
    /// (<see cref="MotionError.UnknownAxis"/>), or the axis is not rotary
    /// (<see cref="MotionError.WrongAxisKind"/>).</exception>
    public static IRotaryAxis Rotary(this IMotionDevice d, string name)
    {
        ArgumentNullException.ThrowIfNull(d);
        return d[name] as IRotaryAxis
               ?? throw new MotionException(MotionError.WrongAxisKind,
                   $"Axis '{name}' on device '{d.Id}' is not a rotary axis.", name);
    }

    /// <summary>Binds a declared name to its linear leaf.</summary>
    /// <param name="d">The device holding the axis.</param>
    /// <param name="name">The plugin-frozen axis name.</param>
    /// <exception cref="MotionException">No axis of that name is declared
    /// (<see cref="MotionError.UnknownAxis"/>), or the axis is not linear
    /// (<see cref="MotionError.WrongAxisKind"/>).</exception>
    public static ILinearAxis Linear(this IMotionDevice d, string name)
    {
        ArgumentNullException.ThrowIfNull(d);
        return d[name] as ILinearAxis
               ?? throw new MotionException(MotionError.WrongAxisKind,
                   $"Axis '{name}' on device '{d.Id}' is not a linear axis.", name);
    }
}
