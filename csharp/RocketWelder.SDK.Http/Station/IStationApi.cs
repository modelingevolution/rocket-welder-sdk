using RocketWelder.SDK.Http.Programs;

namespace RocketWelder.SDK.Http.Station;

/// <summary>
/// <c>/api/station</c> — station-wide settings. Programming defaults are the station layer every program inherits
/// (epic-029 feature-011, design.md §12.4). Writes touch no program file: no etag, no recompile, last writer wins.
/// </summary>
public interface IStationApi
{
    /// <summary><c>GET /api/station/programming-defaults</c> — every index entry with its stored and effective value.</summary>
    Task<StationDefaults> GetProgrammingDefaultsAsync(CancellationToken ct = default);

    /// <summary>
    /// <c>PUT /api/station/programming-defaults/{key}</c> — store a station default under its index key
    /// (e.g. <c>Adaptation.PathTolerance</c>).
    /// </summary>
    /// <exception cref="ValueRefusedException">The value does not parse (HTTP 400); <see cref="ValueRefusedException.Reason"/> is rw2's sentence.</exception>
    /// <exception cref="HttpRequestException">The key is unknown (HTTP 404, the message carries rw2's sentence), or the server failed otherwise.</exception>
    Task<StationDefaults> SetProgrammingDefaultAsync(string key, string value, CancellationToken ct = default);

    /// <summary><c>DELETE /api/station/programming-defaults/{key}</c> — clear a station default; the built-in applies again.</summary>
    /// <exception cref="HttpRequestException">The key is unknown (HTTP 404, the message carries rw2's sentence), or the server failed otherwise.</exception>
    Task<StationDefaults> ClearProgrammingDefaultAsync(string key, CancellationToken ct = default);
}
