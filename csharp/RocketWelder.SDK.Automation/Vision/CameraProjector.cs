using System.Runtime.CompilerServices;
using ModelingEvolution.Drawing;

using Pose3d = ModelingEvolution.Drawing.Pose3<double>;
using Point3d = ModelingEvolution.Drawing.Point3<double>;
using Vector3d = ModelingEvolution.Drawing.Vector3<double>;
using Pointd = ModelingEvolution.Drawing.Point<double>;
using Triangle3d = ModelingEvolution.Drawing.Triangle3<double>;

namespace RocketWelder.SDK.Automation.Vision;

/// <summary>
/// Projects 2D pixel coordinates to 3D points on a surface using camera intrinsics
/// and hand-eye calibration.
/// </summary>
public class CameraProjector : ICameraProjector
{
    private readonly CameraIntrinsics _intrinsics;
    private readonly Pose3d _cameraToGripper;
    private readonly Func<Pose3d> _getCurrentPosition;

    /// <summary>
    /// Creates a camera projector for eye-in-hand setup.
    /// </summary>
    /// <param name="intrinsics">Camera intrinsic parameters (K matrix and distortion).</param>
    /// <param name="cameraToGripper">Calibrated camera-to-gripper transformation.</param>
    /// <param name="getCurrentPosition">Function to get current gripper pose in base frame.</param>
    public CameraProjector(CameraIntrinsics intrinsics, Pose3d cameraToGripper, Func<Pose3d> getCurrentPosition)
    {
        _intrinsics = intrinsics;
        _cameraToGripper = cameraToGripper;
        _getCurrentPosition = getCurrentPosition;
    }

    /// <summary>
    /// Projects a pixel coordinate onto a surface, returning a pose aligned with the surface.
    /// </summary>
    /// <param name="pixel">2D pixel coordinate in image.</param>
    /// <param name="surface">Surface pose in robot base frame (Z-axis is surface normal).</param>
    /// <returns>Pose on surface with position at intersection and orientation aligned with surface.</returns>
    public Pose3d ProjectPose(Pointd pixel, Pose3d surface)
    {
        var point = ProjectPoint(pixel, surface);
        return new Pose3d(point, surface.Rotation);
    }

    /// <summary>
    /// Projects a pixel coordinate onto a surface defined by a triangle.
    /// </summary>
    public Pose3d ProjectPose(Pointd pixel, Triangle3d surface)
        => ProjectPose(pixel, surface.ToPose());

    /// <summary>
    /// Projects a pixel coordinate onto a surface defined by three points (right-hand rule for normal).
    /// </summary>
    public Pose3d ProjectPose(Pointd pixel, Point3d a, Point3d b, Point3d c)
        => ProjectPose(pixel, Pose3d.FromSurface(a, b, c));

    /// <summary>
    /// Projects a pixel coordinate to a point at a fixed distance along the ray.
    /// </summary>
    /// <param name="pixel">2D pixel coordinate in image.</param>
    /// <param name="distance">Distance along the ray from camera origin (in mm).</param>
    /// <returns>Pose with position at the specified distance and Z-axis along ray direction.</returns>
    public Pose3d ProjectPoseAt(Pointd pixel, double distance)
    {
        // 1. Pixel to undistorted ray direction in camera frame
        var dirCam = PixelToRay(pixel);

        // 2. Get current gripper pose
        var gripperToBase = _getCurrentPosition();

        // 3. Compose transforms: camera → gripper → base
        var cameraToBase = gripperToBase * _cameraToGripper;

        // 4. Transform ray to base frame
        var rayOrigin = cameraToBase.Position;
        var rayDir = cameraToBase.TransformVector(dirCam);

        // 5. Position at specified distance along ray
        var position = rayOrigin + rayDir * distance;

        // 6. Build orientation with Z along ray direction
        var rotation = Vector3d.EZ.RotationTo(rayDir);

        return new Pose3d(position, rotation);
    }

    /// <summary>
    /// Computes the 3D correction vector between actual and expected pixel locations at a fixed distance.
    /// </summary>
    /// <param name="actualLocation">Pixel coordinate where feature was actually detected.</param>
    /// <param name="expectedLocation">Pixel coordinate where feature was expected.</param>
    /// <param name="distance">Distance along rays from camera origin (in mm).</param>
    /// <returns>Correction vector (actual - expected) to apply to robot position.</returns>
    public Vector3d ProjectVector(Pointd actualLocation, Pointd expectedLocation, double distance)
    {
        var actualPose = ProjectPoseAt(actualLocation, distance);
        var expectedPose = ProjectPoseAt(expectedLocation, distance);

        return actualPose.Position - expectedPose.Position;
    }

    /// <summary>
    /// Computes the 3D correction vector between actual and expected pixel locations projected onto a surface.
    /// </summary>
    /// <param name="actualLocation">Pixel coordinate where feature was actually detected.</param>
    /// <param name="expectedLocation">Pixel coordinate where feature was expected.</param>
    /// <param name="surface">Surface pose in robot base frame (Z-axis is surface normal).</param>
    /// <returns>Correction vector (actual - expected) to apply to robot position.</returns>
    public Vector3d ProjectVector(Pointd actualLocation, Pointd expectedLocation, Pose3d surface)
    {
        var actualPoint = ProjectPoint(actualLocation, surface);
        var expectedPoint = ProjectPoint(expectedLocation, surface);

        return actualPoint - expectedPoint;
    }

    /// <summary>
    /// Computes the 3D correction vector projected onto a surface defined by a triangle.
    /// </summary>
    public Vector3d ProjectVector(Pointd actualLocation, Pointd expectedLocation, Triangle3d surface)
        => ProjectVector(actualLocation, expectedLocation, surface.ToPose());

    /// <summary>
    /// Computes the 3D correction vector projected onto a surface defined by three points.
    /// </summary>
    public Vector3d ProjectVector(Pointd actualLocation, Pointd expectedLocation, Point3d a, Point3d b, Point3d c)
        => ProjectVector(actualLocation, expectedLocation, Pose3d.FromSurface(a, b, c));

    /// <summary>
    /// Projects a pixel coordinate onto a surface, returning the 3D point in robot base frame.
    /// </summary>
    /// <param name="pixel">2D pixel coordinate in image.</param>
    /// <param name="surface">Surface pose in robot base frame (Z-axis is surface normal).</param>
    /// <returns>3D point on surface in robot base frame.</returns>
    public Point3d ProjectPoint(Pointd pixel, Pose3d surface)
    {
        // 1. Pixel to undistorted ray direction in camera frame
        var dirCam = PixelToRay(pixel);

        // 2. Get current gripper pose
        var gripperToBase = _getCurrentPosition();

        // 3. Compose transforms: camera → gripper → base
        var cameraToBase = gripperToBase * _cameraToGripper;

        // 4. Transform ray to base frame
        var rayOrigin = cameraToBase.Position;
        var rayDir = cameraToBase.TransformVector(dirCam);

        // 5. Get plane from surface pose (point + normal)
        var planePoint = surface.Position;
        var planeNormal = surface.Rotation.Rotate(Vector3d.EZ);

        // 6. Ray-plane intersection: t = ((P0 - O) · N) / (D · N)
        var originToPlane = planePoint - rayOrigin;

        double dotDN = rayDir * planeNormal;
        if (Math.Abs(dotDN) < 1e-9)
            throw new InvalidOperationException("Ray is parallel to surface");

        double t = (originToPlane * planeNormal) / dotDN;

        if (t < 0)
            throw new InvalidOperationException("Intersection is behind camera");

        // 7. Intersection point
        return rayOrigin + rayDir * t;
    }

    /// <summary>
    /// Projects a pixel coordinate onto a surface defined by a triangle.
    /// </summary>
    public Point3d ProjectPoint(Pointd pixel, Triangle3d surface)
        => ProjectPoint(pixel, surface.ToPose());

    /// <summary>
    /// Projects a pixel coordinate onto a surface defined by three points (right-hand rule for normal).
    /// </summary>
    public Point3d ProjectPoint(Pointd pixel, Point3d a, Point3d b, Point3d c)
        => ProjectPoint(pixel, Pose3d.FromSurface(a, b, c));

    /// <inheritdoc />
    public Polyline3<double> ProjectPoints(Polyline<float> pixels, Pose3d surface)
    {
        var span = pixels.AsSpan();
        var points = new Point3d[span.Length];
        for (int i = 0; i < span.Length; i++)
        {
            var pixel = new Pointd(double.CreateChecked(span[i].X), double.CreateChecked(span[i].Y));
            points[i] = ProjectPoint(pixel, surface);
        }
        return new Polyline3<double>(points);
    }

    /// <inheritdoc />
    public Polyline3<double> ProjectPoints(Polyline<float> pixels, Triangle3d surface)
        => ProjectPoints(pixels, surface.ToPose());

    /// <inheritdoc />
    public Polyline3<double> ProjectPoints(Polyline<double> pixels, Pose3d surface)
    {
        var span = pixels.AsSpan();
        var points = new Point3d[span.Length];
        for (int i = 0; i < span.Length; i++)
            points[i] = ProjectPoint(new Pointd(span[i].X, span[i].Y), surface);
        return new Polyline3<double>(points);
    }

    /// <inheritdoc />
    public Polyline3<double> ProjectPoints(Polyline<double> pixels, Triangle3d surface)
        => ProjectPoints(pixels, surface.ToPose());

    /// <inheritdoc />
    public Pose3d GetTcpForCameraPose(Pose3d cameraPose) => cameraPose * _cameraToGripper.Inverse();

    private Vector3d PixelToRay(Pointd pixel) => _intrinsics.PixelToRay(pixel);
}
