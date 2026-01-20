# KeyPoints Binary Protocol Specification

## Overview

The KeyPoints protocol provides efficient binary serialization for arbitrary point data across video frames. It captures the **state** of keypoints without assumptions about their semantic meaning. KeyPoints can represent:
- Pose/skeleton joints
- Segmentation boundary points
- Geometric centers
- Feature points
- Any calculated points from vision pipelines

It uses a two-file system with master/delta frame compression for optimal storage and streaming performance.

## Architecture

### Two-File System

1. **Definition File** (`keypoints.json`):
   - Human-readable JSON with metadata and keypoint mappings
   - **Structure**:
     - `version`: Version of the keypoints algorithm or model (string)
     - `compute_module_name`: Name of AI model or assembly that generates keypoints (string)
     - `points`: Dictionary mapping keypoint names to numeric IDs (object)
   - Shared across all sessions using the same definition
   - Example: `{"version": "1.0", "compute_module_name": "YOLOv8-Pose", "points": {"nose": 0, ...}}`
   - **Note**: The binary protocol doesn't interpret these - it just stores the state

2. **Binary Data File** (`keypoints.bin`):
   - Compact binary format with master/delta frame compression
   - Optimized for streaming
   - Cross-platform compatible (explicit little-endian)
   - **No file header** - just sequential frames

### Frame Types

#### Master Frame (Keyframe)
- Written every N frames (default: 300)
- Contains complete absolute coordinates for all keypoints
- Allows random access and error recovery

#### Delta Frame
- Contains only differences from previous frame
- Uses delta encoding + ZigZag + varint compression
- Significantly smaller than master frames for smooth changes
- Requires previous frame for decoding

## Binary Protocol Format

### Frame Structure

#### Master Frame
```
[FrameType: 1B = 0x00]         // 0x00 = Master Frame
[FrameId: 8B little-endian]
[KeyPointCount: varint]        // Number of keypoints in this frame

For each keypoint:
  [KeyPointId: varint]         // Maps to keypoints.json
  [X: 4B int32 LE]             // Absolute pixel X coordinate
  [Y: 4B int32 LE]             // Absolute pixel Y coordinate
  [Confidence: 2B ushort LE]   // Encoded as 0-10000 (API uses float 0.0-1.0)
```

#### Delta Frame
```
[FrameType: 1B = 0x01]         // 0x01 = Delta Frame
[FrameId: 8B little-endian]
[KeyPointCount: varint]

For each keypoint:
  [KeyPointId: varint]
  [DeltaX: varint]             // ZigZag encoded delta (signed)
  [DeltaY: varint]             // ZigZag encoded delta (signed)
  [ConfidenceDelta: varint]    // ZigZag encoded delta of ushort value (signed)
```

### Frame Boundary Detection

**For stream-based transports** (file, TCP, Unix sockets):
- Each frame is prefixed with its length (varint for files, 4-byte LE for TCP)
- Format: `[length prefix][frame data]`
- No end-of-stream marker needed - EOF or connection close indicates end

**For message-oriented transports** (WebSocket):
- Native message boundaries
- One frame = one message
- No length prefix or end marker needed

## Definition File Format (`keypoints.json`)

The definition file is application-specific and defines what each keypoint ID means. The binary protocol doesn't interpret this - it's purely for human reference and visualization.

### Example 1: Pose/Skeleton Points
```json
{
  "version": "1.0",
  "compute_module_name": "YOLOv8-Pose",
  "points": {
    "nose": 0,
    "left_eye": 1,
    "right_eye": 2,
    "left_ear": 3,
    "right_ear": 4,
    "left_shoulder": 5,
    "right_shoulder": 6,
    "left_elbow": 7,
    "right_elbow": 8,
    "left_wrist": 9,
    "right_wrist": 10,
    "left_hip": 11,
    "right_hip": 12,
    "left_knee": 13,
    "right_knee": 14,
    "left_ankle": 15,
    "right_ankle": 16
  }
}
```

### Example 2: Segmentation-Based Points
```json
{
  "version": "2.1",
  "compute_module_name": "CustomSegmentationModule",
  "points": {
    "segment_1_centroid": 0,
    "segment_1_top_point": 1,
    "segment_1_bottom_point": 2,
    "segment_2_centroid": 3,
    "segment_2_left_point": 4,
    "segment_2_right_point": 5,
    "midpoint_segment_1_2": 6
  }
}
```

### Example 3: Mixed Application
```json
{
  "version": "3.2.1",
  "compute_module_name": "VehicleDetectorV3.dll",
  "points": {
    "vehicle_center": 0,
    "front_left_corner": 1,
    "front_right_corner": 2,
    "rear_left_corner": 3,
    "rear_right_corner": 4,
    "license_plate_center": 5,
    "headlight_left": 6,
    "headlight_right": 7
  }
}
```

## Encoding Details

### Delta Encoding
- Delta values are integer pixel differences
- Example: previous X=100, current X=103 → delta=3
- Encoded using ZigZag + varint compression
- Decoded: `current_value = previous_value + zigzag_decode(varint)`

### Confidence Encoding

**Public API**: Uses `float` (0.0-1.0) for intuitive confidence values

**Binary Storage**: Internally encoded as `ushort` (0-10000) for efficiency
- Encode: `confidence_ushort = (ushort)(confidence_float * 10000)`
- Decode: `confidence_float = confidence_ushort / 10000.0f`
- Precision: 0.01% (0.0001)
- Storage: 2 bytes per confidence value

This encoding is an **implementation detail** - the public IKeyPointsWriter API accepts `float` and the KeyPointsSeries returns `float`.

### ZigZag Encoding
```
Encode: (n << 1) ^ (n >> 31)
Decode: (n >> 1) ^ -(n & 1)
```

### Varint Encoding
- Variable-length integer encoding
- Same format as Protocol Buffers
- 7 bits per byte + continuation bit

## Interface Definitions

### C# Interfaces

```csharp
/// <summary>
/// Factory for creating keypoints writers and reading keypoints data.
/// </summary>
public interface IKeyPointsSink
{
    /// <summary>
    /// Create a writer for the current frame.
    /// Sink decides whether to write master or delta frame.
    /// </summary>
    IKeyPointsWriter CreateWriter(ulong frameId);

    /// <summary>
    /// Read entire keypoints series into memory for efficient querying.
    /// </summary>
    /// <param name="json">JSON definition string mapping keypoint names to IDs</param>
    /// <param name="frameSource">Frame source to read frames from (handles transport-specific framing)</param>
    Task<KeyPointsSeries> Read(string json, IFrameSource frameSource);
}

/// <summary>
/// Writes keypoints data for a single frame to binary stream.
/// Lightweight writer - create one per frame via IKeyPointsStorage.
/// </summary>
public interface IKeyPointsWriter : IDisposable
{
    /// <summary>
    /// Append a keypoint to this frame.
    /// </summary>
    /// <param name="keypointId">KeyPoint identifier</param>
    /// <param name="x">X coordinate in pixels</param>
    /// <param name="y">Y coordinate in pixels</param>
    /// <param name="confidence">Confidence value (0.0-1.0)</param>
    void Append(int keypointId, int x, int y, float confidence);

    /// <summary>
    /// Append a keypoint to this frame.
    /// </summary>
    /// <param name="keypointId">KeyPoint identifier</param>
    /// <param name="p">Point coordinates</param>
    /// <param name="confidence">Confidence value (0.0-1.0)</param>
    void Append(int keypointId, Point p, float confidence);
}

/// <summary>
/// In-memory representation of keypoints series for efficient querying.
/// </summary>
public class KeyPointsSeries
{
    // Internal index: frameId -> (keypointId -> (Point, confidence))
    private Dictionary<ulong, SortedList<int, (Point point, float confidence)>> _index;

    /// <summary>
    /// Version of the keypoints algorithm or model.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Name of AI model or assembly that generated the keypoints.
    /// </summary>
    public string ComputeModuleName { get; }

    /// <summary>
    /// Definition mapping: keypoint name -> keypoint ID
    /// </summary>
    public IReadOnlyDictionary<string, int> Points { get; }

    /// <summary>
    /// Get all frame IDs in the series.
    /// </summary>
    public IReadOnlyCollection<ulong> FrameIds { get; }

    /// <summary>
    /// Get all keypoints for a specific frame.
    /// Returns null if frame not found.
    /// </summary>
    public SortedList<int, (Point point, float confidence)>? GetFrame(ulong frameId);

    /// <summary>
    /// Get trajectory of a specific keypoint across all frames.
    /// Returns enumerable of (frameId, point, confidence) tuples.
    /// Lazily evaluated - efficient for large series.
    /// </summary>
    public IEnumerable<(ulong frameId, Point point, float confidence)> GetKeyPointTrajectory(int keypointId);

    /// <summary>
    /// Get trajectory of a specific keypoint by name across all frames.
    /// Returns enumerable of (frameId, point, confidence) tuples.
    /// Lazily evaluated - efficient for large series.
    /// </summary>
    public IEnumerable<(ulong frameId, Point point, float confidence)> GetKeyPointTrajectory(string keypointName);

    /// <summary>
    /// Check if a frame exists in the series.
    /// </summary>
    public bool ContainsFrame(ulong frameId);

    /// <summary>
    /// Get keypoint position and confidence at specific frame.
    /// Returns null if frame or keypoint not found.
    /// </summary>
    public (Point point, float confidence)? GetKeyPoint(ulong frameId, int keypointId);

    /// <summary>
    /// Get keypoint position and confidence at specific frame by name.
    /// Returns null if frame or keypoint not found.
    /// </summary>
    public (Point point, float confidence)? GetKeyPoint(ulong frameId, string keypointName);
}
```

## Usage Example

### Writing KeyPoints

```csharp
// Create sink with underlying stream
using var fileStream = File.Open("keypoints.bin", FileMode.Create);
using var sink = new KeyPointsSink(fileStream); // Auto-creates StreamFrameSink

for (ulong frameId = 0; frameId < 1000; frameId++)
{
    // Calculate keypoints from your vision pipeline
    var keypoints = CalculateKeyPoints(frame);

    // Create lightweight writer for this frame
    // Sink decides whether to write master or delta frame
    using var writer = sink.CreateWriter(frameId);

    foreach (var kp in keypoints)
    {
        writer.Append(kp.KeyPointId, kp.X, kp.Y, kp.Confidence); // confidence as float 0.0-1.0
    }

    // Frame written on Dispose() via IFrameSink (with varint length prefix for files)
}
```

### Example: Pose Estimation
```csharp
var poseResult = poseEstimator.Detect(frame);

// Create writer for this frame (sink handles master/delta decision)
using var writer = sink.CreateWriter(frameId);

// Append each detected joint
for (int i = 0; i < 17; i++)  // COCO-17 skeleton
{
    writer.Append(
        keypointId: i,
        x: poseResult.Joints[i].X,
        y: poseResult.Joints[i].Y,
        confidence: poseResult.Joints[i].Confidence  // float 0.0-1.0
    );
}
```

### Example: Segmentation Center Points
```csharp
var segments = segmenter.Detect(frame);

// Create writer for this frame (sink handles master/delta decision)
using var writer = sink.CreateWriter(frameId);

// Append centroid for each segment
for (int i = 0; i < segments.Count; i++)
{
    var centroid = segments[i].CalculateCentroid();
    writer.Append(
        keypointId: i,
        x: centroid.X,
        y: centroid.Y,
        confidence: 1.0f  // Always confident for computed points
    );
}
```

### Reading KeyPoints

The sink loads the entire keypoints series into memory via `Read()`, which:
- Parses the JSON definition (keypoint names → IDs)
- Reads frames via IFrameSource (handles length-prefix framing automatically)
- Decodes all master and delta frames into absolute coordinates
- Builds an efficient in-memory index for fast queries
- Supports typical use cases: per-frame access, trajectory analysis, random access

```csharp
// Load definition JSON and binary data
var json = await File.ReadAllTextAsync("keypoints.json");
using var blobStream = File.OpenRead("keypoints.bin");

// Create frame source (handles varint length-prefix framing)
using var frameSource = new StreamFrameSource(blobStream);

// Read entire series into memory
var sink = new KeyPointsSink(blobStream); // For writing (not needed here)
var series = await sink.Read(json, frameSource);

// Metadata from definition
Console.WriteLine($"Model: {series.ComputeModuleName} v{series.Version}");
Console.WriteLine($"KeyPoints defined: {series.Points.Count}");

// Query 1: Iterate through all frames
foreach (var frameId in series.FrameIds)
{
    var keypoints = series.GetFrame(frameId);
    Console.WriteLine($"Frame {frameId}: {keypoints.Count} keypoints");

    foreach (var (keypointId, (point, confidence)) in keypoints)
    {
        // Look up name from points definition
        var name = series.Points.FirstOrDefault(kvp => kvp.Value == keypointId).Key
                   ?? $"Point_{keypointId}";
        Console.WriteLine($"  {name}: ({point.X}, {point.Y}) confidence={confidence:F3}");
    }
}

// Query 2: Get trajectory of a specific keypoint by name (lazy evaluation)
var noseTrajectory = series.GetKeyPointTrajectory("nose");
Console.WriteLine("Nose trajectory:");
foreach (var (frameId, point, confidence) in noseTrajectory)
{
    Console.WriteLine($"  Frame {frameId}: ({point.X}, {point.Y}) conf={confidence:F3}");
}

// Query 3: Get specific keypoint at specific frame by name
var result = series.GetKeyPoint(frameId: 100, keypointName: "nose");
if (result.HasValue)
{
    var (point, confidence) = result.Value;
    Console.WriteLine($"Nose at frame 100: ({point.X}, {point.Y}) conf={confidence:F3}");
}

// Query 4: Get by ID instead of name (also lazy)
var leftEyeTrajectory = series.GetKeyPointTrajectory(keypointId: 1);

// Efficient: Only iterates as needed with LINQ
var first10Frames = leftEyeTrajectory.Take(10);
var filtered = leftEyeTrajectory.Where(t => t.point.X > 100);
var highConfidence = leftEyeTrajectory.Where(t => t.confidence > 0.8f);
var avgX = leftEyeTrajectory.Average(t => t.point.X);

// Direct frame access (no iteration)
var leftEyeResult = series.GetKeyPoint(frameId: 100, keypointId: 1);
if (leftEyeResult.HasValue)
{
    var (point, confidence) = leftEyeResult.Value;
    Console.WriteLine($"Left eye: ({point.X}, {point.Y}) conf={confidence:F3}");
}
```

## Performance Characteristics

### Compression Ratios (Typical - 17 keypoints)
- **Master Frame**: ~153 bytes
  - Frame header: 10 bytes
  - Per keypoint: 8-9 bytes (varint id + 4B X + 4B Y + 2B conf)

- **Delta Frame**: ~42 bytes
  - Frame header: 10 bytes
  - Per keypoint: 2 bytes (varint id + varint delta X/Y + varint conf delta)

- **Compression Ratio**: Delta frames are ~70% smaller

### Master Frame Interval Trade-offs

| Interval | File Size | Error Recovery | Notes                    |
|----------|-----------|----------------|--------------------------|
| 60       | Larger    | Excellent      | 2 seconds @ 30fps        |
| 150      | Medium    | Good           | 5 seconds @ 30fps        |
| 300      | Smaller   | Fair           | 10 seconds @ 30fps ⭐    |
| 600      | Smallest  | Poor           | 20 seconds @ 30fps       |

**Recommended**: 300 frames (10 seconds @ 30fps) - good balance of compression and recovery

### In-Memory Footprint (KeyPointsSeries)

When loaded into memory via `Read()`:
- **Per Point with confidence**: 12 bytes (Point: 2× int32 + float)
- **17 keypoints per frame**: ~204 bytes + dictionary overhead
- **1000 frames @ 17 keypoints**: ~220-280 KB in memory
- **10,000 frames @ 17 keypoints**: ~2.2-2.8 MB in memory

Memory usage is proportional to:
- Number of frames
- Average keypoints per frame
- Does NOT depend on master/delta encoding (all decoded to absolute)
- Confidence stored as `float` (4 bytes) in memory for fast access

### Query Performance

**GetFrame(frameId)**: O(1) - Direct dictionary lookup, returns `SortedList<int, (Point, float)>`
**GetKeyPoint(frameId, keypointId)**: O(1) - Two dictionary lookups, returns `(Point, float)?`
**GetKeyPointTrajectory(keypointId)**: O(N) - Lazy enumeration, no allocation
- Returns `IEnumerable<(ulong frameId, Point point, float confidence)>` - lazy evaluation
- No intermediate collection allocation
- Efficient with LINQ (Take, Where, Average, etc.)
- Only iterates frames where keypoint exists
- Confidence included in tuple for filtering (`Where(t => t.confidence > 0.8f)`)

For large datasets (100K+ frames), the lazy enumeration is critical:
- Can process trajectories without allocating large collections
- LINQ operations can short-circuit (e.g., `Take(10)` only iterates 10 frames)
- Memory-efficient even for long-running analysis

## Cross-Platform Compatibility

### Endianness
- All multi-byte values use **explicit little-endian** encoding
- Use `BinaryPrimitives.WriteInt32LittleEndian()` for coordinates in C#
- Use `BinaryPrimitives.WriteInt64LittleEndian()` for frame IDs in C#
- Use `struct.pack('<i', value)` for coordinates in Python
- Use `struct.pack('<q', value)` for frame IDs in Python

### Varint/ZigZag
- Same implementation as Protocol Buffers
- Language-agnostic encoding
- Identical results across platforms

## File Naming Convention

```
keypoints.json          # Definition file (name → ID mapping)
keypoints.bin           # Binary data (frame stream)
```

## Comparison with Segmentation Protocol

| Feature | Segmentation | KeyPoints |
|---------|-------------|-----------|
| File Header | No | No |
| Frame Types | Single | Master + Delta |
| Coordinate Type | int32 (pixels) | int32 (pixels) |
| Compression | Delta + varint | Delta + varint + master/delta |
| Use Case | Contours (variable points) | Fixed set of tracked points |
| File Count | 1 | 2 (definition + data) |
| Semantic Meaning | None (just points) | None (just state) |

## Design Philosophy

The protocol follows these principles:

1. **State Only**: Capture what IS, not what it MEANS
   - Binary format has no semantic knowledge
   - Definition file is for humans/visualization only

2. **No Metadata**: No timestamps, no instance IDs, no tracking
   - Frame ID is sufficient for correlation
   - Higher-level tracking is application concern

3. **Simple & Fast**: Minimal overhead
   - No file header
   - Straightforward frame structure
   - Easy to implement and debug

4. **Flexible**: Works for any point-based data
   - Pose estimation
   - Segmentation features
   - Tracking points
   - Geometric features
   - Application-defined points

## Error Handling

### Missing Master Frame
- Storage implementation should skip delta frames until next master frame during Read()
- Cannot decode deltas without a baseline
- Recommended: Log warning "Skipped frames {start}-{end}, waiting for master frame"

### Corrupted Delta Chain
- If decode fails during Read(), skip to next master frame
- Master frames provide natural recovery points
- Recommended: Log error with frame ID

### Variable KeyPoint Count
- Different frames can have different keypoint counts
- KeyPointsSeries handles dynamic frame structures automatically
- Common scenarios:
  - KeyPoints appear/disappear as objects enter/leave frame
  - Different objects have different keypoint sets
- GetFrame() returns only keypoints present in that specific frame

---

**Version**: 1.0
**Last Updated**: 2025-12-03
**Authors**: RocketWelder SDK Team
