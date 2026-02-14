# Camera Projector

## Overview

`CameraProjector` projects 2D pixel coordinates to 3D points on a surface using camera intrinsics and hand-eye calibration. This is the final step that combines calibration results for runtime vision-guided robot control.

## Prerequisites

1. **Camera intrinsics** calibrated (see `CameraCalibration.md`)
2. **Hand-eye calibration** completed (see `HandEyeCalibrations.md`)
3. **Work surface** defined by 3 points in robot base frame

## API

```csharp
public class CameraProjector
{
    /// <summary>
    /// Creates a camera projector for eye-in-hand setup.
    /// </summary>
    public CameraProjector(
        CameraIntrinsics intrinsics,
        Pose3<double> cameraToGripper,
        Func<Pose3<double>> getCurrentPosition
    );

    /// <summary>
    /// Projects a pixel coordinate onto a surface, returning a pose aligned with the surface.
    /// </summary>
    /// <returns>Pose on surface with position at intersection and orientation aligned with surface.</returns>
    public Pose3<double> ProjectPose(Point<double> pixel, Pose3<double> surface);
    public Pose3<double> ProjectPose(Point<double> pixel, Triangle3<double> surface);
    public Pose3<double> ProjectPose(Point<double> pixel, Point3<double> a, Point3<double> b, Point3<double> c);

    /// <summary>
    /// Projects a pixel coordinate onto a surface, returning the 3D point in robot base frame.
    /// </summary>
    /// <returns>3D point on surface in robot base frame.</returns>
    public Point3<double> ProjectPoint(Point<double> pixel, Pose3<double> surface);
    public Point3<double> ProjectPoint(Point<double> pixel, Triangle3<double> surface);
    public Point3<double> ProjectPoint(Point<double> pixel, Point3<double> a, Point3<double> b, Point3<double> c);

    /// <summary>
    /// Projects a pixel coordinate to a point at a fixed distance along the ray.
    /// </summary>
    /// <param name="pixel">2D pixel coordinate in image.</param>
    /// <param name="distance">Distance along the ray from camera origin (in mm).</param>
    /// <returns>Pose with position at specified distance and Z-axis along ray direction.</returns>
    public Pose3<double> ProjectPoseAt(Point<double> pixel, double distance);

    /// <summary>
    /// Computes the 3D correction vector between actual and expected pixel locations at a fixed distance.
    /// </summary>
    /// <returns>Correction vector (actual - expected) to apply to robot position.</returns>
    public Vector3<double> ProjectVector(Point<double> actualLocation, Point<double> expectedLocation, double distance);

    /// <summary>
    /// Computes the 3D correction vector between actual and expected pixel locations projected onto a surface.
    /// </summary>
    /// <returns>Correction vector (actual - expected) to apply to robot position.</returns>
    public Vector3<double> ProjectVector(Point<double> actualLocation, Point<double> expectedLocation, Pose3<double> surface);
    public Vector3<double> ProjectVector(Point<double> actualLocation, Point<double> expectedLocation, Triangle3<double> surface);
    public Vector3<double> ProjectVector(Point<double> actualLocation, Point<double> expectedLocation, Point3<double> a, Point3<double> b, Point3<double> c);
}
```

## Setup

### 1. Load Calibration Data

```csharp
// Camera intrinsics (from CameraCalibration)
var intrinsics = new CameraIntrinsics(
    fx: 1000, fy: 1000,     // focal lengths in pixels
    cx: 640, cy: 360,       // principal point
    k1: 0, k2: 0, k3: 0,    // radial distortion
    p1: 0, p2: 0            // tangential distortion
);

// Hand-eye calibration result (from HandEyeCalibration)
var cameraToGripper = new Pose3<double>(
    x: 50, y: 0, z: 100,    // translation in mm
    rx: 0, ry: 0, rz: -90   // rotation in degrees
);
```

### 2. Create the Projector

```csharp
var projector = new CameraProjector(
    intrinsics,
    cameraToGripper,
    () => cobot.GetActualPose()  // live position from robot
);
```

### 3. Define Work Surface

Measure 3 corner points of the work surface in robot base frame:

```csharp
// Three corners of the welding table (in robot base frame, mm)
var cornerA = new Point3<double>(400, -200, 50);   // origin
var cornerB = new Point3<double>(600, -200, 50);   // defines X axis
var cornerC = new Point3<double>(400, 200, 50);    // defines plane

// Create surface pose using right-hand rule
// Z-axis points up from surface (a->b->c counter-clockwise)
var surface = Pose3<double>.FromSurface(cornerA, cornerB, cornerC);
```

**Note**: Point order determines Z direction via right-hand rule:
- `FromSurface(a, b, c)` - Z points "up" (counter-clockwise winding)
- `FromSurface(c, b, a)` - Z points "down" (clockwise winding)

### Surface Definition Options

All surface-based methods accept three interchangeable surface definitions:

```csharp
// Option 1: Pose3 (pre-computed)
var surface = Pose3<double>.FromSurface(cornerA, cornerB, cornerC);
var point = projector.ProjectPoint(pixel, surface);

// Option 2: Triangle3 (when you have a triangle)
var triangle = new Triangle3<double>(cornerA, cornerB, cornerC);
var point = projector.ProjectPoint(pixel, triangle);

// Option 3: Three points directly (most convenient)
var point = projector.ProjectPoint(pixel, cornerA, cornerB, cornerC);
```

## Runtime Usage

### Basic Projection

```csharp
// Get detected pixel from vision system
Point<double> pixel = imageProcessor.GetKeyPoint();

// Project to 3D world coordinates
Point3<double> worldPoint = projector.ProjectPoint(pixel, surface);

Console.WriteLine($"Detected at: X={worldPoint.X:F2}, Y={worldPoint.Y:F2}, Z={worldPoint.Z:F2}");
```

### Full Example: Vision-Guided Welding

```csharp
// Setup (once at startup)
var imageProcessor = new ImageProcessor("http://127.0.0.1:5000");
var projector = new CameraProjector(intrinsics, cameraToGripper, () => cobot.GetActualPose());
var surface = Pose3<double>.FromSurface(cornerA, cornerB, cornerC);

const double torchHeight = 15.0; // mm above surface

// Runtime loop
while (welding)
{
    // 1. Detect weld seam in image
    Point<double> seamPixel = imageProcessor.GetKeyPoint();

    // 2. Project to robot pose (position + orientation aligned with surface)
    Pose3<double> seamPose = projector.ProjectPose(seamPixel, surface);

    // 3. Add torch height offset along Z
    var targetPose = seamPose + new Vector3<double>(0, 0, torchHeight);

    // 4. Move robot
    cobot.MoveCart(targetPose, velocity: 50f);
}
```

### Distance-Based Projection

Use `ProjectPoseAt` when you know the distance to the target but not the surface plane:

```csharp
// Move to a point 200mm along the camera ray from detected pixel
Point<double> targetPixel = imageProcessor.GetKeyPoint();
double knownDistance = 200.0; // mm from camera

// Get pose at distance with Z-axis pointing along the ray
Pose3<double> approachPose = projector.ProjectPoseAt(targetPixel, knownDistance);

// The Z-axis of the returned pose points along the ray direction
// Useful for approach movements or when surface orientation is unknown
cobot.MoveCart(approachPose, velocity: 30f);
```

### Real-Time Path Correction

Use `ProjectVector` to compute corrections when tracking a feature:

```csharp
// During path execution, compare expected vs actual feature position
Point<double> expectedPixel = pathPlanner.GetExpectedPixel();
Point<double> actualPixel = imageProcessor.GetKeyPoint();

// Get 3D correction vector (on surface)
Vector3<double> correction = projector.ProjectVector(actualPixel, expectedPixel, surface);

// Or at known distance (e.g., when surface is unknown)
Vector3<double> correction = projector.ProjectVector(actualPixel, expectedPixel, distance: 300);

// Apply correction to current target
var correctedTarget = currentTarget + correction;
cobot.MoveCart(correctedTarget, velocity: 50f);
```

## Projection Pipeline

### Surface Projection (`ProjectPoint`, `ProjectPose`)

```
pixel (u, v)
     |
     v Intrinsics K (undistort + to ray)
camera ray direction
     |
     v Hand-eye transform (camera -> gripper)
gripper frame ray
     |
     v Robot kinematics (gripper -> base)
base frame ray
     |
     v Ray-plane intersection
3D point on surface (robot base frame)
```

### Distance Projection (`ProjectPoseAt`)

```
pixel (u, v)
     |
     v Intrinsics K (undistort + to ray)
camera ray direction
     |
     v Hand-eye transform (camera -> gripper)
gripper frame ray
     |
     v Robot kinematics (gripper -> base)
base frame ray
     |
     v Ray origin + distance x ray direction
3D pose with Z along ray (robot base frame)
```

## Error Handling

```csharp
try
{
    var worldPoint = projector.ProjectPoint(pixel, surface);
}
catch (InvalidOperationException ex) when (ex.Message.Contains("parallel"))
{
    // Ray is parallel to surface - camera looking along the plane
    Console.WriteLine("Cannot project: camera ray parallel to surface");
}
catch (InvalidOperationException ex) when (ex.Message.Contains("behind"))
{
    // Intersection is behind camera - surface is behind the camera
    Console.WriteLine("Cannot project: surface is behind camera");
}
```

## References

- `CameraCalibration.md` - Camera intrinsic calibration
- `HandEyeCalibrations.md` - Hand-eye calibration procedures
- `ImageProcessor` - Vision system API for keypoint detection
