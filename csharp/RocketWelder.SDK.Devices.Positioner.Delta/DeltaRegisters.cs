namespace RocketWelder.SDK.Devices.Positioner.Delta;

/// <summary>
/// Modbus map of a Delta VFD-C2000 running the positioner's PLC program.
///
/// <para>
/// Two Modbus stations live behind one TCP endpoint: station 1 exposes the drive's own parameters
/// (<c>Pr.gg-nn</c> at <c>group*256 + number</c>) and station 2 exposes the built-in PLC's devices
/// (chapter 16-5-4 of the C2000 manual).
/// </para>
///
/// <para>
/// <b>Required PLC program</b> (ISPSoft, identical on every axis drive). Without it the drive
/// answers Modbus but does not move:
/// <code>
///   M0 -> M1025 (RUN) + M1040 (servo on)
///   M4 -> edges: MOV 0/1 D1060 (speed/position mode); FREQ D110 D111 D112
///   M5 -> M1026 (direction: OFF = forward, ON = reverse)
///   M6 + edge X7 -> DSUB D1051 D120 D122; DMOV D1051 D120   (home latch)
/// </code>
/// The ladder is an external artefact and cannot be verified over Modbus, which is why
/// <see cref="DeltaAxis.HomeAsync"/> writes a sentinel before arming the latch: it is the only way
/// to tell "the latch did not run" from "the latch stored the same value again".
/// </para>
/// </summary>
public static class DeltaRegisters
{
    /// <summary>Modbus TCP port.</summary>
    public const int Port = 502;

    /// <summary>Station of the drive itself (<c>Pr.09-00</c>) — drive parameters.</summary>
    public const byte DriveUnit = 1;

    /// <summary>Station of the built-in PLC (<c>Pr.09-35</c>) — M/D/X devices.</summary>
    public const byte PlcUnit = 2;

    // ── PLC devices (station 2) ───────────────────────────────────

    /// <summary>D110 — commanded frequency for FREQ, in 0.01 Hz.</summary>
    public const ushort D110_Frequency = 0x1000 + 110;

    /// <summary>D111/D112 — acceleration and deceleration ramps, in 0.01 s.</summary>
    public const ushort D111_Ramp = 0x1000 + 111;

    /// <summary>D120/D121 — position latched on the home-sensor edge (DWORD).</summary>
    public const ushort D120_HomeLatch = 0x1000 + 120;

    /// <summary>D122/D123 — DSUB result written alongside the latch (DWORD, diagnostics).</summary>
    public const ushort D122_LatchDelta = 0x1000 + 122;

    /// <summary>D1051/D1052 — current position from the PG card (DWORD, signed).</summary>
    public const ushort D1051_Position = 0x1000 + 1051;

    /// <summary>X0 — first digital input, read as discrete inputs.</summary>
    public const ushort X0_Inputs = 0x0400;

    /// <summary>Number of X inputs read in one transaction.</summary>
    public const ushort InputCount = 10;

    /// <summary>M0 — RUN + servo on.</summary>
    public const ushort M0_Run = 0x0800 + 0;

    /// <summary>M4 — motion (FREQ) on/off.</summary>
    public const ushort M4_Move = 0x0800 + 4;

    /// <summary>M5 — direction (OFF = forward, ON = reverse).</summary>
    public const ushort M5_Direction = 0x0800 + 5;

    /// <summary>M6 — arms the home-position latch.</summary>
    public const ushort M6_ArmLatch = 0x0800 + 6;

    // ── Drive registers (station 1) ───────────────────────────────

    /// <summary>Output frequency, in 0.01 Hz.</summary>
    public const ushort OutputFrequency = 0x2103;

    /// <summary>Fault code in the low byte.</summary>
    public const ushort FaultCode = 0x2100;

    /// <summary>Command word; bit 1 resets a fault.</summary>
    public const ushort CommandWord = 0x2002;

    /// <summary>Pr.02-04 — MI4 function (44 = lower travel limit).</summary>
    public const ushort Pr0204_Mi4 = 0x0204;

    /// <summary>Pr.02-05 — MI5 function (45 = upper travel limit).</summary>
    public const ushort Pr0205_Mi5 = 0x0205;

    /// <summary>Value of <see cref="Pr0204_Mi4"/> that enables the lower limit.</summary>
    public const ushort Mi4LimitFunction = 44;

    /// <summary>Value of <see cref="Pr0205_Mi5"/> that enables the upper limit.</summary>
    public const ushort Mi5LimitFunction = 45;

    /// <summary>
    /// Drive parameters the controller enforces at startup, so behaviour does not depend on what
    /// somebody last typed into the keypad.
    ///
    /// <para>
    /// The encoder sits BEHIND the gearbox and does not close the speed loop (VFPG with correction
    /// off), so the PG-feedback fault detections would only produce spurious errors from backlash
    /// and creep. Position is derived by this controller, not by the drive.
    /// </para>
    ///
    /// <para>
    /// The limit-switch functions are enforced here too. They are temporarily cleared while jogging
    /// off a tripped limit, and if the process dies inside that window the drive would otherwise
    /// stay unprotected until somebody noticed.
    /// </para>
    /// </summary>
    public static IReadOnlyList<(ushort Address, ushort Value, string Why)> RequiredSetup { get; } =
    [
        (0x000A, 0, "Pr.00-10 = speed mode (position is derived by this controller)"),
        (0x000B, 1, "Pr.00-11 = VFPG"),
        (0x0A08, 0, "Pr.10-08 = PGF1/2 no reaction"),
        (0x0A0A, 0, "Pr.10-10 = PGF3 off"),
        (0x0A0D, 0, "Pr.10-13 = PGF4 off"),
        (0x0A1D, 0, "Pr.10-29 = no PG speed correction"),
    ];

    /// <summary>
    /// Limit-switch functions, enforced ONLY on axes that actually have limit switches. Writing
    /// these onto an endless rotary axis would turn an unrelated input into a travel limit.
    ///
    /// <para>
    /// They are re-asserted at startup because retreating from a tripped limit has to lift them
    /// temporarily, and a process killed inside that window would otherwise leave the drive running
    /// without hardware limit protection.
    /// </para>
    /// </summary>
    public static IReadOnlyList<(ushort Address, ushort Value, string Why)> LimitSetup { get; } =
    [
        (Pr0204_Mi4, Mi4LimitFunction, "Pr.02-04 = MI4 is the lower travel limit"),
        (Pr0205_Mi5, Mi5LimitFunction, "Pr.02-05 = MI5 is the upper travel limit"),
    ];
}
