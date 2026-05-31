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
public interface ICameraProjector
{
    /// <summary>
    /// Projects a pixel coordinate onto a surface, returning a pose aligned with the surface.
    /// </summary>
    Pose3d ProjectPose(Pointd pixel, Pose3d surface);

    /// <summary>
    /// Projects a pixel coordinate onto a surface defined by a triangle.
    /// </summary>
    Pose3d ProjectPose(Pointd pixel, Triangle3d surface);

    /// <summary>
    /// Projects a pixel coordinate onto a surface defined by three points (right-hand rule for normal).
    /// </summary>
    Pose3d ProjectPose(Pointd pixel, Point3d a, Point3d b, Point3d c);

    /// <summary>
    /// Projects a pixel coordinate to a point at a fixed distance along the ray.
    /// </summary>
    Pose3d ProjectPoseAt(Pointd pixel, double distance);

    /// <summary>
    /// Computes the 3D correction vector between actual and expected pixel locations at a fixed distance.
    /// </summary>
    Vector3d ProjectVector(Pointd actualLocation, Pointd expectedLocation, double distance);

    /// <summary>
    /// Computes the 3D correction vector between actual and expected pixel locations projected onto a surface.
    /// </summary>
    Vector3d ProjectVector(Pointd actualLocation, Pointd expectedLocation, Pose3d surface);

    /// <summary>
    /// Computes the 3D correction vector projected onto a surface defined by a triangle.
    /// </summary>
    Vector3d ProjectVector(Pointd actualLocation, Pointd expectedLocation, Triangle3d surface);

    /// <summary>
    /// Computes the 3D correction vector projected onto a surface defined by three points.
    /// </summary>
    Vector3d ProjectVector(Pointd actualLocation, Pointd expectedLocation, Point3d a, Point3d b, Point3d c);

    /// <summary>
    /// Projects a pixel coordinate onto a surface, returning the 3D point in robot base frame.
    /// </summary>
    Point3d ProjectPoint(Pointd pixel, Pose3d surface);

    /// <summary>
    /// Projects a pixel coordinate onto a surface defined by a triangle.
    /// </summary>
    Point3d ProjectPoint(Pointd pixel, Triangle3d surface);

    /// <summary>
    /// Projects a pixel coordinate onto a surface defined by three points (right-hand rule for normal).
    /// </summary>
    Point3d ProjectPoint(Pointd pixel, Point3d a, Point3d b, Point3d c);

    /// <summary>
    /// Projects all points of a 2D polyline (pixel coordinates) onto a surface,
    /// returning a 3D polyline in robot base frame.
    /// </summary>
    Polyline3<double> ProjectPoints(Polyline<float> pixels, Pose3d surface);

    /// <summary>
    /// Projects all points of a 2D polyline (pixel coordinates) onto a surface defined by a triangle.
    /// </summary>
    Polyline3<double> ProjectPoints(Polyline<float> pixels, Triangle3d surface);

    /// <summary>
    /// Projects all points of a 2D polyline (double pixel coordinates) onto a surface,
    /// returning a 3D polyline in robot base frame.
    /// </summary>
    Polyline3<double> ProjectPoints(Polyline<double> pixels, Pose3d surface);

    /// <summary>
    /// Projects all points of a 2D polyline (double pixel coordinates) onto a surface defined by a triangle.
    /// </summary>
    Polyline3<double> ProjectPoints(Polyline<double> pixels, Triangle3d surface);

    /// <summary>
    /// Computes the robot TCP pose that positions the camera at the specified pose in base frame.
    /// Uses the hand-eye calibration: tcp = cameraPose * inv(cameraToGripper).
    /// </summary>
    /// <param name="cameraPose">Desired camera pose in robot base frame.</param>
    /// <returns>Robot TCP pose that achieves the desired camera position and orientation.</returns>
    Pose3d GetTcpForCameraPose(Pose3d cameraPose);

    /// <summary>
    /// Projects a 3D point in robot base frame to a 2D pixel in the current camera image.
    /// The inverse of <see cref="ProjectPoint(Pointd, Pose3d)"/>: it folds in the current
    /// gripper pose and hand-eye calibration to place the point in camera frame, then applies
    /// the pinhole + Brown-Conrady model.
    /// </summary>
    /// <param name="basePoint">Point in robot base frame (mm).</param>
    /// <param name="pixel">The projected pixel when the point is in front of the camera; otherwise <c>default</c>.</param>
    /// <returns><c>true</c> when the point is in front of the camera (camera-frame Z &gt; 0) and a pixel
    /// is produced; <c>false</c> when it is on or behind the image plane. Callers must clip the returned
    /// pixel to the actual viewport bounds — an in-front point can still project outside the image.</returns>
    bool TryProjectToPixel(Point3d basePoint, out Pointd pixel);
}
