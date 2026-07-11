namespace RocketWelder.SDK.Http.Cameras;

/// <summary>
/// <c>/api/cameras/{name?}/...</c> — read-only access to registered cameras.
/// </summary>
public interface ICamerasApi
{
    /// <summary>
    /// <c>GET /api/cameras/{name?}/calibration</c> — current intrinsic + hand-eye
    /// calibration. Null when the camera has no accepted calibration.
    /// </summary>
    Task<CameraCalibration?> GetCalibrationAsync(string? name = null, CancellationToken ct = default);

    /// <summary>
    /// <c>GET /api/cameras/{name?}/frame</c> — JPEG snapshot. The server resizes
    /// to a configured max edge (default 640 px per design §10 open item) before
    /// base64-encoding to keep chat payloads small.
    /// </summary>
    Task<CameraFrame?> GetFrameAsync(string? name = null, CancellationToken ct = default);
}
