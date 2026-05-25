namespace RocketWelder.SDK.Http.Cameras;

/// <summary>
/// A snapshot from a registered camera. JPEG-encoded, base64-wrapped so the
/// LLM can include it directly in chat without a multi-part response.
/// Returned by <c>GET /api/cameras/{name?}/frame</c>.
/// </summary>
/// <param name="JpegBase64">Base64-encoded JPEG bytes (no data-URI prefix).</param>
/// <param name="Width">Pixel width of the encoded frame after any server-side resize.</param>
/// <param name="Height">Pixel height.</param>
/// <param name="CapturedAtUtc">When the frame was pulled from the camera, UTC.</param>
public sealed record CameraFrame(
    string JpegBase64,
    int Width,
    int Height,
    DateTime CapturedAtUtc);
