using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace RocketWelder.SDK.Devices.Motion.Delta;

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
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Creates a store backed by <paramref name="path"/>.</summary>
    /// <param name="path">The JSON file holding every axis's persisted state.</param>
    /// <param name="logger">Optional logger. Worth supplying: losing this file is how an axis
    /// silently forgets where zero is, and the log is the only account of it having happened.</param>
    public JsonAxisStateStore(string path, ILogger? logger = null)
    {
        _path = path;
        _logger = logger;
    }

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
        if (!File.Exists(_path)) return Empty();
        try
        {
            var text = await File.ReadAllTextAsync(_path, ct);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, AxisPersistedState>>(text, Json);

            // Re-key through the case-insensitive comparer. Deserialisation always builds an
            // ORDINAL dictionary regardless of what it is assigned to, so a file written as
            // "Turntable" would stop answering to "turntable" — a silent unhomed axis.
            return parsed is null ? Empty() : new Dictionary<string, AxisPersistedState>(parsed,
                StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException ex)
        {
            // A corrupt file means the zero is unknown; starting from empty forces a re-home, which
            // is the safe reading of "we do not know where zero is". LOUDLY, though: an axis that
            // silently forgets zero will drive confidently to a wrong absolute angle, and this is
            // the only moment anyone can be told it happened.
            _logger?.LogError(ex,
                "Axis state file {Path} could not be parsed. Every stored zero is being treated as "
                + "lost, so each axis will refuse absolute moves until it is re-homed", _path);
            return Empty();
        }
    }

    private static Dictionary<string, AxisPersistedState> Empty() =>
        new(StringComparer.OrdinalIgnoreCase);
}
