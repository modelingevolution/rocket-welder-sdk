namespace RocketWelder.SDK.Devices.Robot;

/// <summary>
/// Vendor-agnostic weave configuration carried to a robot's <see cref="IWeavingRobot.BeginWeave"/>. Mirrors the
/// Fairino pendant's Weave editor: a driver maps these fields onto the controller's weave-parameter call (Fairino:
/// <c>WeaveSetPara</c>). Distinct from the unrelated <c>RocketWelder.SDK.Operations.Weave</c> weld-segment profile.
/// </summary>
/// <param name="SwingType">Oscillation shape; its int value equals the controller's swing-type code.</param>
/// <param name="Instruction">Instruction family selecting the start/end method pair.</param>
/// <param name="FrequencyHz">Wobble frequency in Hz.</param>
/// <param name="AmplitudeMm">Swing amplitude in mm.</param>
/// <param name="IncludeDwell">Whether the left/right dwell times apply ("Swing wait time").</param>
/// <param name="LeftDwellMs">Left-side dwell time in ms.</param>
/// <param name="RightDwellMs">Right-side dwell time in ms.</param>
/// <param name="StationaryWait">
/// Whether the torch oscillates in place ("Swing position waiting" = Stationary). <see cref="WeaveInstruction.FixedPoint"/>
/// forces this on regardless of the stored value; a driver reconciles it.
/// </param>
/// <param name="AzimuthDeg">Swing direction azimuth in degrees.</param>
/// <param name="RollDeg">Roll angle in the swing direction, in degrees.</param>
public sealed record WeaveProfile(
    WeaveSwingType SwingType,
    WeaveInstruction Instruction,
    double FrequencyHz,
    double AmplitudeMm,
    bool IncludeDwell,
    int LeftDwellMs,
    int RightDwellMs,
    bool StationaryWait,
    double AzimuthDeg,
    double RollDeg);
