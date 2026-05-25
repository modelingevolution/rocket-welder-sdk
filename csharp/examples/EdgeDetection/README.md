# EdgeDetection

Sink-only RocketWelder SDK example container. Consumes a BGR video frame stream and
publishes `SegmentationInstanceF`-shaped events containing the sub-pixel-refined
vertices of every cube edge it can find.

Companion to the `BallDetection` example. Used by Epic 029 (Adaptive Points)
iteration 4's manual end-to-end test against the rocket-welder2 simulator.

## What it does

For every input frame:

1. `Cv2.CvtColor(BGR → GRAY)`.
2. `Cv2.Canny(gray, edges, t1, t2)` — hysteresis edge detection.
3. `Cv2.FindContours(edges, RetrievalModes.External, ApproxSimple)`.
4. Filter contours: drop those outside `[MinContourArea, MaxContourArea]` or with
   fewer than `MinVertices` vertices.
5. Refine each remaining vertex via `Cv2.CornerSubPix(gray, vertices, (5,5), (-1,-1), Term(30, 0.01))`.
6. Compute a contour-quality `Confidence` (see "Confidence formula" below).
7. Emit one `SegmentationInstanceF`-equivalent per surviving contour via
   `ISegmentationResultWriter.Append(ClassId, instanceId, confidence, Point[])`.
   `Points[0]` is the contour's first vertex (EdgeStart per FR-5.2).

## Configuration

`appsettings.json`:

```json
{
  "EdgeDetection": {
    "ClassId": 1,
    "CannyThreshold1": 50,
    "CannyThreshold2": 150,
    "MinContourArea": 100.0,
    "MaxContourArea": 100000.0,
    "MinVertices": 4
  }
}
```

Override any field via env var or `--EdgeDetection:Key=Value` arg (standard
`Microsoft.Extensions.Configuration` precedence).

`ClassId` must match the value the operator passes via
`PassCompleted.FeatureClassIds` in the rocket-welder2 adaptive-points flow.
The default `1` is the convention for "cube-edge".

## Confidence formula

`Confidence = ContourArea / ConvexHullArea`, clamped to `[0, 1]`.

* Convex shapes (a clean cube face seen from a reasonable angle) → ratio ≈ 1.0.
* Self-intersecting or jagged contours → lower ratio.
* By construction (since `ContourArea ≤ ConvexHullArea`) the value is bounded in `[0, 1]`.

Confidence is computed from the sub-pixel-refined vertex set (after
`CornerSubPix`), so the metric reflects post-refinement contour quality.
This is what FR-2.5's tiebreaker will see at the consumer side once the higher-
confidence candidate is selected for an adaptive point.

## Precision floor — read this if your residual is > 0.5 mm

The SDK's segmentation wire protocol encodes contour vertices as zig-zag
varint **integers** (see `RocketWelder.SDK/SegmentationProtocolF.cs`). The
`SegmentationInstanceF.Points` field on the read side is `Point<float>`, but
the float values are promoted from the decoded ints with `.0` fractional parts.

What this means for adaptive points: `CornerSubPix`'s sub-pixel refinement is
**rounded to integer at the writer boundary** before transmission. At typical
standoff distances (300–500 mm) with focal length around 1200 px, the integer
quantization contributes roughly 0.2–0.4 mm to the final residual before any
other pipeline noise.

The NR-2 < 0.5 mm target therefore has only modest headroom against the wire
itself. If you observe an adaptive-points residual greater than 0.5 mm during
the manual end-to-end run, the wire-format quantization is a suspect; check
this container's logs for the pre-round refined `PointF` values versus the
post-round integer `Point` values to confirm or rule out wire quantization as
the bottleneck.

A float-precision SDK writer is tracked separately and is out of scope for
this iteration.

## Run alongside rocket-welder2 + simulator (manual sim test)

Per the iteration 4 manual procedure
(`docs/epics/epic-029-adaptive-points/iterations/iteration-4/manual-test-procedure.md`):

1. Bring up the sim and EventStore:
   ```bash
   docker compose up sim eventstore
   ```
2. Start rocket-welder2 host: `./run.sh`
3. Register a `Simulator`-type robot and an `MjpegCamera` peripheral pointing at
   the sim's `/camera/0/preview` MJPEG endpoint.
4. Bring up this container with a `SegmentationSinkUrl` aimed at the
   rocket-welder2 adaptive-points consumer:
   ```bash
   docker run --rm \
     -e RocketWelder__SegmentationSinkUrl=tcp://host.docker.internal:5099 \
     -e RocketWelder__VideoSourceUrl=mjpeg://host.docker.internal:5100/camera/0/preview \
     -e EdgeDetection__ClassId=1 \
     edge-detection:dev
   ```
5. Continue the manual procedure from step 5 ("Teach a point") onward in
   `manual-test-procedure.md`.

## Local dev build

```bash
cd csharp
dotnet build examples/EdgeDetection/EdgeDetection.csproj
```

Run the unit tests against the synthetic cube:

```bash
dotnet test examples/EdgeDetection.Tests/EdgeDetection.Tests.csproj
```

Build the container image:

```bash
cd csharp
docker build -t edge-detection:dev -f examples/EdgeDetection/Dockerfile .
```
