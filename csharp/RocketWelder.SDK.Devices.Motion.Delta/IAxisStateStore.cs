using System.Collections.Concurrent;

namespace RocketWelder.SDK.Devices.Motion.Delta;

/// <summary>
/// Persists the little state that must outlive the process: the captured zero and the chosen
/// traverse speed.
///
/// <para>
/// <b>This survives a restart of the controller, NOT a power cycle of the drive.</b> The encoder
/// count restarts from zero when the drive powers up, so a stored zero from before that is
/// meaningless. Implementations cannot detect this; callers must re-home after drive power-up.
/// </para>
///
/// <para>
/// <b>Why this lives in the adapter and not in the contract.</b> A derived-position axis is a Delta
/// speciality: the drive runs in speed mode because the encoders sit behind the gearboxes, so the
/// zero is a number this adapter computes and owns. An axis whose drive knows its own absolute
/// position has nothing to store. Putting it on <c>IMotionAxis</c> would make every implementation
/// carry a lifecycle that only one of them has. (OQ-4 — where the file lives on a device deployment
/// and who backs it up — is still open; losing it forces a re-home, which is safe but surprising.)
/// </para>
/// </summary>
public interface IAxisStateStore
{
    /// <summary>Loads persisted state, or <see langword="null"/> when nothing is stored for that axis.</summary>
    Task<AxisPersistedState?> LoadAsync(string axis, CancellationToken ct = default);

    /// <summary>Stores state for an axis. Must be atomic — a torn write loses the zero.</summary>
    Task SaveAsync(string axis, AxisPersistedState state, CancellationToken ct = default);
}

/// <summary>State that outlives the controller process.</summary>
/// <param name="ZeroOffset">Raw encoder count captured at the home position.</param>
/// <param name="Homed">Whether that zero is valid.</param>
/// <param name="SpeedDegPerSecond">Traverse speed last chosen, in the axis's own unit.</param>
public sealed record AxisPersistedState(long ZeroOffset, bool Homed, double SpeedDegPerSecond);

/// <summary>
/// Keeps state in memory only — everything is lost on restart, so the axis must re-home.
///
/// <para>
/// A positioner's axes home concurrently (<c>HomeAllAsync</c>), so this really is written from
/// several threads at once. Backed by a concurrent collection rather than a lock around a plain
/// dictionary, per the org's thread-safe-collection standard.
/// </para>
/// </summary>
public sealed class InMemoryAxisStateStore : IAxisStateStore
{
    private readonly ConcurrentDictionary<string, AxisPersistedState> _state =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public Task<AxisPersistedState?> LoadAsync(string axis, CancellationToken ct = default) =>
        Task.FromResult(_state.TryGetValue(axis, out var s) ? s : null);

    /// <inheritdoc/>
    public Task SaveAsync(string axis, AxisPersistedState state, CancellationToken ct = default)
    {
        _state[axis] = state;
        return Task.CompletedTask;
    }
}
