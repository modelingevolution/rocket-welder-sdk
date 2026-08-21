namespace RocketWelder.SDK.Devices.Motion.Delta;

/// <summary>
/// Modbus map of a Delta VFD-C2000 running the positioner's PLC program.
///
/// <para>
/// Two Modbus stations live behind one TCP endpoint: station 1 exposes the drive's own parameters
/// (<c>Pr.gg-nn</c> at <c>group*256 + number</c>) and station 2 exposes the built-in PLC's devices
/// (chapter 16-5-4 of the C2000 manual). A request carrying any other unit id gets <b>no
/// response</b> — the drive is silent rather than exceptional, and the simulator reproduces that.
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
///
/// <para>
/// <b>DSUB runs before DMOV</b> — the subtraction sees the OLD <c>D120</c>. That order is
/// load-bearing and the adapter's homing sequence depends on it: a correctly working latch writes
/// the same number back when the axis returns to the same cam edge, so only a sentinel
/// distinguishes "latched again" from "never ran".
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

    /// <summary>D111/D112 — acceleration and deceleration ramps, in 0.01 s per 50 Hz.</summary>
    public const ushort D111_Ramp = 0x1000 + 111;

    /// <summary>D120/D121 — position latched on the home-sensor edge (DWORD, low word first).</summary>
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

    // ── FR-11 watchdog block (station 2) ──────────────────────────

    /// <summary>
    /// D130 — heartbeat. The commanding process writes a <b>changing</b> value here at ≥ 5 Hz for as
    /// long as its connection lives (FR-11); one second without a change trips the drive's watchdog
    /// network.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Never write 0 as a heartbeat value.</b> The network arms on the first CHANGE of this
    /// register and the register powers up at 0, so a commander whose first beat is literally 0
    /// writes no change and leaves the watchdog disarmed — silently unprotected. Arming is therefore
    /// the first NON-ZERO write, and <see cref="DeltaHeartbeat"/> starts its counter at 1 and
    /// pre-increments past 0 on wrap.
    /// </para>
    /// <para>
    /// Address provenance: chosen in <c>delta-positioner-sim</c> (<c>docs/register-map.md</c>) in the
    /// same free D block the existing program already uses for D110–D122. FR-11 says these addresses
    /// "join the documented ladder register map" but names none, and Daniel's raw port has none to
    /// copy. They are <b>proposed, pending ratification at the AC-25 ladder edit</b>; if the vendor's
    /// ISPSoft project puts them elsewhere, this block changes and nothing else does.
    /// </para>
    /// </remarks>
    public const ushort D130_Heartbeat = 0x1000 + 130;

    /// <summary>
    /// D131 — advisory lease owner: a station-unique 16-bit id from host config, 0 meaning unowned.
    /// See <see cref="AdvisoryLease"/> for the rule that reads it.
    /// </summary>
    public const ushort D131_OwnerId = 0x1000 + 131;

    /// <summary>
    /// D132 — latched watchdog fault: <see cref="WatchdogHealthy"/> or
    /// <see cref="WatchdogHeartbeatStall"/>. Cleared only by the client writing 0; it never clears
    /// itself, or it would not be a latch.
    /// </summary>
    /// <remarks>
    /// The watchdog lives in the PLC, and the PLC cannot write the drive's own fault word
    /// (<see cref="FaultCode"/>) — that word is produced by the drive's firmware. So FR-11's
    /// "distinguishable fault code" deliberately does NOT appear where drive faults do.
    /// </remarks>
    public const ushort D132_WatchdogFault = 0x1000 + 132;

    /// <summary>D133 — watchdog trips since power-up (diagnostics).</summary>
    public const ushort D133_WatchdogTripCount = 0x1000 + 133;

    /// <summary>Value of <see cref="D132_WatchdogFault"/> meaning no watchdog fault is latched.</summary>
    public const ushort WatchdogHealthy = 0;

    /// <summary>
    /// Value of <see cref="D132_WatchdogFault"/> meaning the heartbeat stalled: the run state was
    /// dropped, the commanded frequency zeroed and the limit functions re-asserted. The home latch is
    /// untouched, so recovery is reset + re-command and never a re-home.
    /// </summary>
    public const ushort WatchdogHeartbeatStall = 1;

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
    /// without hardware limit protection (risk R-2). FR-11's watchdog re-asserts them on a trip for
    /// the same reason, which bounds that window at the 1 s stall instead of "until somebody
    /// notices".
    /// </para>
    /// </summary>
    public static IReadOnlyList<(ushort Address, ushort Value, string Why)> LimitSetup { get; } =
    [
        (Pr0204_Mi4, Mi4LimitFunction, "Pr.02-04 = MI4 is the lower travel limit"),
        (Pr0205_Mi5, Mi5LimitFunction, "Pr.02-05 = MI5 is the upper travel limit"),
    ];
}
