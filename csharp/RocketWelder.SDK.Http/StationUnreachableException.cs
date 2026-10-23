using System.Net.Http;

namespace RocketWelder.SDK.Http;

/// <summary>
/// Thrown when the station cannot be reached (epic-128 feature-001 design.md §4, ADR-13). The message names the station's
/// base address and says whether the request may have been applied:
/// <list type="bullet">
/// <item>a connection or name-resolution failure — "Station unreachable at {base}: {reason}. Nothing was done.";</item>
/// <item>no answer within the client's timeout — "Station at {base} did not answer within {timeout}; the request may or
/// may not have been applied — read the state before retrying."</item>
/// </list>
/// </summary>
public sealed class StationUnreachableException : HttpRequestException
{
    private StationUnreachableException(HttpRequestError error, Uri station, bool mayHaveBeenApplied, string message, Exception inner)
        : base(error, message, inner)
    {
        Station = station;
        MayHaveBeenApplied = mayHaveBeenApplied;
    }

    /// <summary>The station's base address the request was sent to.</summary>
    public Uri Station { get; }

    /// <summary><see langword="false"/> when the request never reached the station (nothing was done);
    /// <see langword="true"/> when the station did not answer in time, so the request may or may not have been applied.</summary>
    public bool MayHaveBeenApplied { get; }

    internal static StationUnreachableException NothingDone(Uri station, HttpRequestException cause) =>
        new(cause.HttpRequestError, station, mayHaveBeenApplied: false,
            $"Station unreachable at {station}: {cause.Message.TrimEnd('.')}. Nothing was done.", cause);

    internal static StationUnreachableException NoAnswer(Uri station, TimeSpan timeout, Exception cause) =>
        new(HttpRequestError.Unknown, station, mayHaveBeenApplied: true,
            $"Station at {station} did not answer within {timeout.TotalSeconds:0.###} s; the request may or may not have " +
            "been applied — read the state before retrying.", cause);
}
