using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RocketWelder.SDK.Abstractions;
using RocketWelder.SDK.Automation;

namespace RocketWelder.SDK.Devices.Positioner.Delta.Plugin;

/// <summary>
/// Turns a host <see cref="ConfigSet"/> into a running <see cref="DeltaPositioner"/>, and declares
/// which fields the "Add device" dialog should ask for.
///
/// <para>
/// The dialog asks only for what varies between installations. Gear ratios, encoder counts, register
/// addresses and ramp constants are properties of this machine family, not choices — an installer
/// picking them would be an installer getting them wrong.
/// </para>
/// </summary>
public static class DeltaPositionerRegistration
{
    /// <summary>Device-type discriminator. Persisted in the event store — never change it.</summary>
    public const string DeviceType = "DeltaPositioner_VFDC2000";

    /// <summary>Fields shown in the "Add device" dialog.</summary>
    public static ConfigPropertySchema[] BuildSchemas() =>
    [
        new(TiltHostProperty.Name, "Tilt drive host", "string",
            Required: true, Default: "192.168.2.34", Group: "Connection"),
        new(TurntableHostProperty.Name, "Turntable drive host", "string",
            Required: true, Default: "192.168.2.35", Group: "Connection"),

        new(TiltMinDegProperty.Name, "Tilt lower limit (°)", "double",
            Required: false, Default: "-45", Group: "Travel"),
        new(TiltMaxDegProperty.Name, "Tilt upper limit (°)", "double",
            Required: false, Default: "90", Group: "Travel"),

        // Defaults are the values measured on the first machine. They are a starting point, not a
        // constant of nature: re-measure per machine, because the error they remove is largest at
        // the low speeds circumferential welding uses.
        new(TurntableSpeedSlopeProperty.Name, "Turntable speed slope ((°/s)/Hz)", "double",
            Required: false, Default: "0.5435", Group: "Calibration"),
        new(TurntableSpeedInterceptProperty.Name, "Turntable speed intercept (°/s)", "double",
            Required: false, Default: "-0.199", Group: "Calibration"),

        new(AxisStatePathProperty.Name, "Axis state file", "string",
            Required: false, Default: "/var/lib/rocketwelder/positioner-axes.json", Group: "State"),
    ];

    /// <summary>Builds the device from stored configuration.</summary>
    public static object Build(ConfigSet config, DeviceId id, IServiceProvider services)
    {
        var loggerFactory = services.GetService<ILoggerFactory>();

        var tiltHost = config.Get<TiltHostProperty, string>();
        var turntableHost = config.Get<TurntableHostProperty, string>();
        if (string.IsNullOrWhiteSpace(tiltHost) || string.IsNullOrWhiteSpace(turntableHost))
            throw new InvalidOperationException(
                $"{DeviceType}: both axis drive hosts must be configured");

        var tiltMin = config.Get<TiltMinDegProperty, double>();
        var tiltMax = config.Get<TiltMaxDegProperty, double>();
        var slope = config.Get<TurntableSpeedSlopeProperty, double>();
        var intercept = config.Get<TurntableSpeedInterceptProperty, double>();
        var statePath = config.Get<AxisStatePathProperty, string>();

        var tilt = DeltaPositionerDefaults.Tilt with
        {
            Host = tiltHost,
            // 0 means "not set" for a double property, and both limits being 0 would be a machine
            // that cannot move — so fall back rather than trust it.
            Min = tiltMin == 0 && tiltMax == 0 ? DeltaPositionerDefaults.Tilt.Min : tiltMin,
            Max = tiltMin == 0 && tiltMax == 0 ? DeltaPositionerDefaults.Tilt.Max : tiltMax,
        };

        var turntable = DeltaPositionerDefaults.Turntable with
        {
            Host = turntableHost,
            Speed = slope > 0
                ? new SpeedCalibration(slope, intercept)
                : DeltaPositionerDefaults.Turntable.Speed,
        };

        if (slope <= 0)
        {
            loggerFactory?.CreateLogger(typeof(DeltaPositionerRegistration))
                .LogInformation(
                    "{Device}: no turntable speed calibration in config — using the built-in figures "
                    + "measured on the first machine ({Slope} (deg/s)/Hz, {Intercept} deg/s). Slip and "
                    + "dead band vary between machines, so re-measure this one if travel speed matters.",
                    id,
                    DeltaPositionerDefaults.Turntable.Speed!.Value.Slope,
                    DeltaPositionerDefaults.Turntable.Speed!.Value.Intercept);
        }

        IAxisStateStore store = string.IsNullOrWhiteSpace(statePath)
            ? new InMemoryAxisStateStore()
            : new JsonAxisStateStore(statePath);

        return new DeltaPositioner(id, [tilt, turntable], store,
            loggerFactory?.CreateLogger<DeltaPositioner>());
    }
}

/// <summary>
/// Mechanical constants of the two-axis Delta positioner. Established by measurement on the machine;
/// wrong for a differently built one, which is why they live in code rather than in a dialog.
/// </summary>
public static class DeltaPositionerDefaults
{
    /// <summary>Tilt axis — limited travel, homing required, limit switches on MI4/MI5.</summary>
    public static DeltaAxisConfig Tilt { get; } = new()
    {
        Name = "tilt",
        DisplayName = "Tilt",
        Host = "192.168.2.34",
        Min = -45.0,
        Max = 90.0,
        Continuous = false,
        RequiresHoming = true,
        CountsPerRevolution = 100_000,
        GearRatio = 79.2,                 // Pr.10-04/05 = 7920/100
        InvertAngle = true,
        SeekHz = 8.0,
        MoveHz = 25.0,
        MaxMoveHz = 50.0,
        MinJogHz = 2.5,
        NudgeHz = 3.0,
        // Carried over unverified. The turntable's equivalent turned out to be wrong by 2x plus a
        // dead time, so treat this as a placeholder until measured on the tilt axis too.
        Pulse = new PulseCalibration(Slope: 0.7, DeadTime: 0.0),
        Tolerance = 0.10,
        HomeSensorInput = 7,
        LimitInputs = (Min: 5, Max: 6),
        // Measured on this axis: the stepped cascade crawled every move under ~17 deg at 2.5 Hz
        // (0.57 deg/s), so 5-15 deg moves took 21-49 s. Continuous deceleration does the same moves
        // in 2.7-3.7 s with equal or better accuracy.
        // NOT yet exercised against a tripped travel limit — mid-range and long moves only.
        SmoothApproach = true,
        SmoothDecelerationFraction = 0.7,
        // Smaller than the turntable's: this axis hands over at a much lower speed, so it coasts
        // ~0.06 deg rather than ~0.5 deg after the handover.
        SmoothHandover = 0.3,
    };

    /// <summary>Turntable — endless rotary axis, no limit switches.</summary>
    public static DeltaAxisConfig Turntable { get; } = new()
    {
        Name = "turntable",
        DisplayName = "Turntable",
        Host = "192.168.2.35",
        Min = 0.0,
        Max = 360.0,
        Continuous = true,
        RequiresHoming = false,
        CountsPerRevolution = 100_000,
        GearRatio = 32.26,                // Pr.10-04/05 = 3226/100
        InvertAngle = false,
        SeekHz = 10.0,
        MoveHz = 30.0,
        MaxMoveHz = 50.0,
        MinJogHz = 1.0,
        NudgeHz = 2.0,
        // Measured: 0.15 s -> 0.036°, 0.25 s -> 0.108°, 0.40 s -> 0.216°
        Pulse = new PulseCalibration(Slope: 0.72, DeadTime: 0.10),
        Tolerance = 0.05,
        HomeSensorInput = 7,
        LimitInputs = null,
        SmoothApproach = true,
        SmoothDecelerationFraction = 0.7,
        SmoothHandover = 0.8,
        Speed = new SpeedCalibration(Slope: 0.5435, Intercept: -0.199),
    };
}
