namespace RocketWelder.SDK.Automation;

/// <summary>
/// Classification of a <see cref="DistanceReading"/>. Default(0) is <see cref="NoReading"/>
/// so a zero-initialised reading is automatically the "no data" sentinel.
/// </summary>
public enum DistanceStatus : byte
{
    /// <summary>The sensor has not yet produced a successful reading (or last connection was lost).</summary>
    NoReading = 0,

    /// <summary>The measured offset falls within the operator's tolerance window.</summary>
    InRange = 1,

    /// <summary>The measured offset falls outside the operator's tolerance window.</summary>
    OutOfRange = 2,
}

/// <summary>
/// Atomic snapshot of one sensor reading. Carries the measured absolute distance plus
/// the operator's tolerance window (in offset units, relative to the sensor's
/// sweetspot) so consumers see a self-consistent record — no race between
/// "reading updated" and "config updated".
/// </summary>
/// <param name="MeasuredDistanceMM">Absolute distance from the sensor face (mm). <c>0</c> only when <see cref="Status"/> is <see cref="DistanceStatus.NoReading"/>.</param>
/// <param name="TargetDistanceMM">Snapshot of the sensor's configured sweetspot (mm).</param>
/// <param name="MinOffsetMM">Snapshot of the operator's tolerance floor (signed offset, mm).</param>
/// <param name="MaxOffsetMM">Snapshot of the operator's tolerance ceiling (signed offset, mm).</param>
/// <param name="ReadAt">UTC timestamp of the read (or <c>default</c> when <see cref="Status"/> is <see cref="DistanceStatus.NoReading"/>).</param>
/// <param name="Status">Classification of this reading.</param>
public readonly record struct DistanceReading(
    double MeasuredDistanceMM,
    double TargetDistanceMM,
    double MinOffsetMM,
    double MaxOffsetMM,
    DateTime ReadAt,
    DistanceStatus Status)
{
    /// <summary>Canonical "no reading yet" sentinel — all fields default, <see cref="Status"/> = <see cref="DistanceStatus.NoReading"/>.</summary>
    public static DistanceReading None => default;

    /// <summary>
    /// Positive = farther than the target sweetspot, negative = closer.
    /// Defined only when <see cref="IsValid"/> is true.
    /// </summary>
    public double MeasuredOffsetFromTargetInMM => MeasuredDistanceMM - TargetDistanceMM;

    /// <summary>Lower bound of the in-range window in absolute distance (mm). Computed: <c>Target + MinOffset</c>.</summary>
    public double MinDistanceMM => TargetDistanceMM + MinOffsetMM;

    /// <summary>Upper bound of the in-range window in absolute distance (mm). Computed: <c>Target + MaxOffset</c>.</summary>
    public double MaxDistanceMM => TargetDistanceMM + MaxOffsetMM;

    /// <summary><c>true</c> when this reading is classified <see cref="DistanceStatus.InRange"/>.</summary>
    public bool IsInRange => Status == DistanceStatus.InRange;

    /// <summary><c>true</c> when at least one successful read has been performed (Status is not <see cref="DistanceStatus.NoReading"/>).</summary>
    public bool IsValid => Status != DistanceStatus.NoReading;

    /// <summary>
    /// Value equality excluding <see cref="ReadAt"/>. Two readings have the same measurement
    /// when <see cref="Status"/> and <see cref="MeasuredDistanceMM"/> agree (the Target/Min/Max
    /// config snapshot cannot drift between successive polls — config changes rebuild the
    /// sensor and reset the poll loop). Use this for UI-update gating and event-firing
    /// decisions; the default record-struct <see cref="Equals(DistanceReading)"/> includes
    /// <see cref="ReadAt"/> and is therefore unsuitable for change detection.
    /// </summary>
    public bool HasSameMeasurement(DistanceReading other) =>
        Status == other.Status
        && EqualityComparer<double>.Default.Equals(MeasuredDistanceMM, other.MeasuredDistanceMM);

    /// <summary>
    /// Factory: classify a measured value against a config snapshot. <paramref name="minOffset"/>
    /// and <paramref name="maxOffset"/> are signed offsets relative to <paramref name="target"/>.
    /// </summary>
    public static DistanceReading From(
        double measuredMM,
        double target,
        double minOffset,
        double maxOffset,
        DateTime readAt)
    {
        var offset = measuredMM - target;
        var status = (offset >= minOffset && offset <= maxOffset)
            ? DistanceStatus.InRange
            : DistanceStatus.OutOfRange;
        return new DistanceReading(measuredMM, target, minOffset, maxOffset, readAt, status);
    }
}
