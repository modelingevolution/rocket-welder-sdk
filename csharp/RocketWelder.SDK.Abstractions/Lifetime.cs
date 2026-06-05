namespace RocketWelder.SDK.Abstractions;

/// <summary>
/// How long a <see cref="IProgramContext.GetData{T}"/> / <see cref="IProgramContext.SetData{T}"/>
/// value lives. The runtime owns the boundary between runs.
/// </summary>
public enum Lifetime
{
    /// <summary>
    /// Survives across runs (default). Cleared only by explicit delete.
    /// Typical use: program-level memory ("last calibration date") or inter-program shared bag.
    /// </summary>
    Permanent,

    /// <summary>
    /// Bound to the current run. Survives pause/stop within the run; cleared when a new run starts.
    /// Typical use: per-run resume state (e.g. "seam_3_done").
    /// </summary>
    Run
}
