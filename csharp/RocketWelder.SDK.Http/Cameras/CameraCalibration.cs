namespace RocketWelder.SDK.Http.Cameras;

/// <summary>
/// Wire shape for a camera's intrinsic + hand-eye calibration. Backed by
/// the server's calibration read-model. Wire-flat to keep the protocol stable
/// across calibration-library evolution.
/// </summary>
/// <param name="SerialNumber">Camera identifier (typically the device serial).</param>
/// <param name="Fx">Focal length in pixels, x-axis.</param>
/// <param name="Fy">Focal length in pixels, y-axis.</param>
/// <param name="Cx">Principal point x, in pixels.</param>
/// <param name="Cy">Principal point y, in pixels.</param>
/// <param name="Distortion">Distortion coefficients (k1, k2, p1, p2, k3, ...). Length depends on the model the server fit.</param>
/// <param name="RmsError">Reprojection RMS error from the fit. Lower is better.</param>
/// <param name="BoundRobotName">Robot whose TCP frame the hand-eye is in, or null when no hand-eye is associated.</param>
/// <param name="IsAccepted">True when the operator has accepted this calibration for production use.</param>
public sealed record CameraCalibration(
    string SerialNumber,
    double Fx,
    double Fy,
    double Cx,
    double Cy,
    IReadOnlyList<double> Distortion,
    double RmsError,
    string? BoundRobotName,
    bool IsAccepted);
