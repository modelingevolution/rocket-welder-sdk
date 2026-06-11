using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.AdaptivePoints;

/// <summary>
/// State of one endpoint's offset at traversal time: either a correction read from a prior
/// adaptation, or zero when that endpoint had no current resolution. The two cases are distinct
/// even when the numeric offset is zero — the operator reads them as full / partial / no
/// correction (FR-2.5). Failure is never expressed here; see <see cref="TraverseReport"/>.
/// </summary>
public abstract record OffsetState
{
    private OffsetState() { }

    /// <summary>The endpoint had a current resolution. <paramref name="Offset"/> = corrected − taught.</summary>
    public sealed record Resolved(Vector3<double> Offset) : OffsetState;

    /// <summary>The endpoint had no current resolution; it contributed a zero offset.</summary>
    public sealed record Zero : OffsetState;
}
