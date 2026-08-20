namespace RocketWelder.SDK.Devices.Positioner;

/// <summary>
/// Persists the little state that must outlive the process: the captured zero and the chosen
/// traverse speed.
///
/// <para>
/// <b>This survives a restart of the controller, NOT a power cycle of the drive.</b> The encoder
/// count restarts from zero when the drive powers up, so a stored zero from before that is
/// meaningless. Implementations cannot detect this; callers must re-home after drive power-up.
/// </para>
/// </summary>
public interface IAxisStateStore
{
    /// <summary>Loads persisted state, or <c>null</c> when nothing is stored for that axis.</summary>
    Task<AxisPersistedState?> LoadAsync(string axis, CancellationToken ct = default);

    /// <summary>Stores state for an axis. Must be atomic — a torn write loses the zero.</summary>
    Task SaveAsync(string axis, AxisPersistedState state, CancellationToken ct = default);
}

/// <summary>State that outlives the controller process.</summary>
/// <param name="ZeroOffset">Raw encoder count captured at the home position.</param>
/// <param name="Homed">Whether that zero is valid.</param>
/// <param name="SpeedDegPerSecond">Traverse speed chosen by the operator.</param>
public sealed record AxisPersistedState(long ZeroOffset, bool Homed, double SpeedDegPerSecond);

/// <summary>Keeps state in memory only — everything is lost on restart, so the axis must re-home.</summary>
public sealed class InMemoryAxisStateStore : IAxisStateStore
{
    private readonly Dictionary<string, AxisPersistedState> _state = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public Task<AxisPersistedState?> LoadAsync(string axis, CancellationToken ct = default)
    {
        lock (_state)
            return Task.FromResult(_state.TryGetValue(axis, out var s) ? s : null);
    }

    /// <inheritdoc/>
    public Task SaveAsync(string axis, AxisPersistedState state, CancellationToken ct = default)
    {
        lock (_state)
            _state[axis] = state;
        return Task.CompletedTask;
    }
}
