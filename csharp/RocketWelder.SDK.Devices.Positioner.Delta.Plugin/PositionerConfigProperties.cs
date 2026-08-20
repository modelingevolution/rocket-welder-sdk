using RocketWelder.SDK.Automation;

namespace RocketWelder.SDK.Devices.Positioner.Delta.Plugin;

/// <summary>Host address of the tilt-axis drive.</summary>
public record TiltHostProperty(string Value)
    : ConfigProperty<string, TiltHostProperty>(Value), IConfigProperty<TiltHostProperty>
{
    /// <inheritdoc cref="IConfigProperty{T}.Name"/>
    public static string Name => "TiltHost";
}

/// <summary>Host address of the turntable-axis drive.</summary>
public record TurntableHostProperty(string Value)
    : ConfigProperty<string, TurntableHostProperty>(Value), IConfigProperty<TurntableHostProperty>
{
    /// <inheritdoc cref="IConfigProperty{T}.Name"/>
    public static string Name => "TurntableHost";
}

/// <summary>Lower travel limit of the tilt axis, in degrees.</summary>
public record TiltMinDegProperty(double Value)
    : ConfigProperty<double, TiltMinDegProperty>(Value), IConfigProperty<TiltMinDegProperty>
{
    /// <inheritdoc cref="IConfigProperty{T}.Name"/>
    public static string Name => "TiltMinDeg";
}

/// <summary>Upper travel limit of the tilt axis, in degrees.</summary>
public record TiltMaxDegProperty(double Value)
    : ConfigProperty<double, TiltMaxDegProperty>(Value), IConfigProperty<TiltMaxDegProperty>
{
    /// <inheritdoc cref="IConfigProperty{T}.Name"/>
    public static string Name => "TiltMaxDeg";
}

/// <summary>
/// Measured slope of turntable speed against motor frequency, in (°/s)/Hz.
/// <para>Leave unset only if you have not measured it — the theoretical figure overstates the
/// speed badly at the low end, which is where circumferential welding runs.</para>
/// </summary>
public record TurntableSpeedSlopeProperty(double Value)
    : ConfigProperty<double, TurntableSpeedSlopeProperty>(Value), IConfigProperty<TurntableSpeedSlopeProperty>
{
    /// <inheritdoc cref="IConfigProperty{T}.Name"/>
    public static string Name => "TurntableSpeedSlope";
}

/// <summary>Measured intercept of turntable speed against motor frequency, in °/s (negative = dead band).</summary>
public record TurntableSpeedInterceptProperty(double Value)
    : ConfigProperty<double, TurntableSpeedInterceptProperty>(Value), IConfigProperty<TurntableSpeedInterceptProperty>
{
    /// <inheritdoc cref="IConfigProperty{T}.Name"/>
    public static string Name => "TurntableSpeedIntercept";
}

/// <summary>File holding the captured zero across restarts.</summary>
public record AxisStatePathProperty(string Value)
    : ConfigProperty<string, AxisStatePathProperty>(Value), IConfigProperty<AxisStatePathProperty>
{
    /// <inheritdoc cref="IConfigProperty{T}.Name"/>
    public static string Name => "AxisStatePath";
}
