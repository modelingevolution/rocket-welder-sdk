using System.Text.Json;

namespace RocketWelder.SDK.Devices.Positioner.Delta;

/// <summary>
/// Keeps axis state in one JSON file.
///
/// <para>
/// Writes go to a temporary file and are then swapped in, because a torn write loses the captured
/// zero — and an axis that silently forgets where zero is will happily drive to a wrong absolute
/// angle.
/// </para>
/// </summary>
public sealed class JsonAxisStateStore : IAxisStateStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Creates a store backed by <paramref name="path"/>.</summary>
    public JsonAxisStateStore(string path) => _path = path;

    /// <inheritdoc/>
    public async Task<AxisPersistedState?> LoadAsync(string axis, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return (await ReadAllAsync(ct)).GetValueOrDefault(axis);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task SaveAsync(string axis, AxisPersistedState state, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var all = await ReadAllAsync(ct);
            all[axis] = state;

            var directory = Path.GetDirectoryName(Path.GetFullPath(_path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var temp = _path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(all, Json), ct);
            File.Move(temp, _path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, AxisPersistedState>> ReadAllAsync(CancellationToken ct)
    {
        if (!File.Exists(_path)) return new Dictionary<string, AxisPersistedState>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var text = await File.ReadAllTextAsync(_path, ct);
            return JsonSerializer.Deserialize<Dictionary<string, AxisPersistedState>>(text, Json)
                   ?? new Dictionary<string, AxisPersistedState>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            // A corrupt file means the zero is unknown; starting from empty forces a re-home, which
            // is the safe reading of "we do not know where zero is".
            return new Dictionary<string, AxisPersistedState>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
