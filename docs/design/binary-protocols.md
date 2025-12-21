# RocketWelder.SDK.BinaryProtocols Design Document

## Overview

This document describes the design of `RocketWelder.SDK.BinaryProtocols`, a WASM-compatible package providing symmetric read/write abstractions for RocketWelder streaming protocols.

## Goals

1. **Full Round-Trip Support**: Enable encoding AND decoding of all protocols in a single package
2. **WASM Compatibility**: Work in Blazor WASM without any platform-specific dependencies
3. **Zero-Copy Performance**: Use `IBufferWriter<byte>` and `ReadOnlySpan<byte>` for high performance
4. **API Symmetry**: Readers and Writers mirror each other for intuitive usage
5. **Transport Independence**: Pure protocol logic, no transport dependencies

## Package Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                    RocketWelder.SDK.BinaryProtocols                  │
│                         (WASM Compatible)                            │
├─────────────────────────────────────────────────────────────────────┤
│  Segmentation/                    │  Keypoints/                      │
│  ├── SegmentationFrame           │  ├── KeypointFrame               │
│  ├── SegmentationInstance        │  ├── Keypoint                    │
│  ├── SegmentationReader          │  ├── KeypointReader (stateful)   │
│  └── SegmentationWriter          │  └── KeypointWriter (stateful)   │
├─────────────────────────────────────────────────────────────────────┤
│  Core/                                                               │
│  ├── BinaryFrameReader (ref struct, zero-allocation)                │
│  ├── BinaryFrameWriter (ref struct, zero-allocation)                │
│  └── VarintExtensions (encode/decode varints)                       │
└─────────────────────────────────────────────────────────────────────┘
                                    │
                                    │ Uses
                                    ▼
┌─────────────────────────────────────────────────────────────────────┐
│                         RocketWelder.SDK                             │
│                       (NOT WASM Compatible)                          │
├─────────────────────────────────────────────────────────────────────┤
│  Transport/                                                          │
│  ├── IFrameSink / IFrameSource                                      │
│  ├── UnixSocketFrameSink / UnixSocketFrameSource                    │
│  ├── NngFrameSink / NngFrameSource                                  │
│  ├── StreamFrameSink / StreamFrameSource                            │
│  └── NullFrameSink                                                   │
├─────────────────────────────────────────────────────────────────────┤
│  High-Level/                                                         │
│  ├── RocketWelderClient (orchestration)                             │
│  ├── FrameSinkFactory (transport creation)                          │
│  └── ConnectionStrings (URL parsing)                                │
└─────────────────────────────────────────────────────────────────────┘
```

## Namespace

**Package Name**: `RocketWelder.SDK.BinaryProtocols`
**Target Framework**: `net10.0`
**NuGet ID**: `RocketWelder.SDK.BinaryProtocols`

## Data Structures

### Segmentation

```csharp
namespace RocketWelder.SDK.BinaryProtocols.Segmentation;

/// <summary>
/// A single segmentation instance (polygon) within a frame.
/// </summary>
public readonly struct SegmentationInstance
{
    public byte ClassId { get; init; }
    public byte InstanceId { get; init; }
    public ReadOnlyMemory<Point> Points { get; init; }
}

/// <summary>
/// Complete segmentation frame with all instances.
/// </summary>
public readonly struct SegmentationFrame
{
    public ulong FrameId { get; init; }
    public uint Width { get; init; }
    public uint Height { get; init; }
    public ReadOnlyMemory<SegmentationInstance> Instances { get; init; }
}
```

### Keypoints

```csharp
namespace RocketWelder.SDK.BinaryProtocols.Keypoints;

/// <summary>
/// A single keypoint with position and confidence.
/// </summary>
public readonly struct Keypoint
{
    public int Id { get; init; }
    public Point Position { get; init; }
    public ushort Confidence { get; init; }  // 0-10000 (scaled from 0.0-1.0)

    public float ConfidenceFloat => Confidence / 10000f;
}

/// <summary>
/// Complete keypoint frame.
/// </summary>
public readonly struct KeypointFrame
{
    public ulong FrameId { get; init; }
    public bool IsDelta { get; init; }
    public ReadOnlyMemory<Keypoint> Keypoints { get; init; }
}
```

## Reader API

### Design Decision: Streaming Callbacks vs Eager Parsing

After TDD validation and research (Utf8JsonReader patterns, MessagePack-CSharp), we provide **two complementary APIs**:

1. **Streaming API (Primary)** - Zero-allocation, callback-based, ideal for rendering loops
2. **Eager API (Secondary)** - Returns complete frames, allocates, ideal for testing/debugging

### SegmentationReader - Streaming API (RECOMMENDED)

```csharp
namespace RocketWelder.SDK.BinaryProtocols.Segmentation;

/// <summary>
/// Zero-allocation streaming reader for segmentation frames.
/// Points are passed directly to callback for immediate rendering.
/// </summary>
public static class SegmentationReader
{
    public delegate void HeaderCallback(in SegmentationHeader header);
    public delegate void InstanceCallback(in SegmentationInstanceData instance, ReadOnlySpan<Point> points);
    public delegate void CompleteCallback();

    /// <summary>
    /// Parse frame with streaming callbacks. Zero allocation.
    /// Points buffer is reused across instances.
    /// </summary>
    public static void Parse(
        ReadOnlySpan<byte> data,
        Span<Point> pointBuffer,
        HeaderCallback onHeader,
        InstanceCallback onInstance,
        CompleteCallback onComplete);
}

/// <summary>
/// Header data passed to callback.
/// </summary>
public readonly struct SegmentationHeader
{
    public ulong FrameId { get; init; }
    public uint Width { get; init; }
    public uint Height { get; init; }
}

/// <summary>
/// Instance data passed to callback. Ref struct for efficiency.
/// Points are passed as separate parameter, not captured.
/// </summary>
public readonly ref struct SegmentationInstanceData
{
    public byte ClassId { get; init; }
    public byte InstanceId { get; init; }
    public uint PointCount { get; init; }
}
```

**Usage in WASM Rendering Loop:**
```csharp
// Pre-allocate buffer (once, reuse across frames)
Span<Point> pointBuffer = stackalloc Point[4096];

SegmentationReader.Parse(frameData, pointBuffer,
    onHeader: (in SegmentationHeader h) =>
    {
        _stage.OnFrameStart(h.FrameId);
        _stage.Clear(_layerId);
    },
    onInstance: (in SegmentationInstanceData inst, ReadOnlySpan<Point> points) =>
    {
        // Points passed directly - no need to access shared buffer!
        var color = _palette[inst.ClassId];
        _canvas.DrawPolygon(points, color);
    },
    onComplete: () =>
    {
        _stage.OnFrameEnd();
    });
```

### SegmentationReader - Eager API (Alternative)

```csharp
/// <summary>
/// Eager parsing API - allocates, returns complete frame.
/// Use for testing, debugging, or when deferred processing is needed.
/// </summary>
public static class SegmentationReader
{
    /// <summary>
    /// Parse a complete segmentation frame from binary data.
    /// Allocates memory for instances and points.
    /// </summary>
    public static SegmentationFrame Parse(ReadOnlySpan<byte> data);

    /// <summary>
    /// Try to parse a frame, returning false if data is incomplete.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> data, out SegmentationFrame frame, out int bytesConsumed);
}
```

### KeypointReader - Streaming API (RECOMMENDED)

```csharp
namespace RocketWelder.SDK.BinaryProtocols.Keypoints;

/// <summary>
/// Stateful streaming reader for keypoint frames.
/// Maintains previous frame state for delta decoding.
/// Class (not ref struct) because state persists across frames.
/// </summary>
public class KeypointReader
{
    public delegate void HeaderCallback(in KeypointHeader header);
    public delegate void KeypointCallback(in KeypointData keypoint);
    public delegate void CompleteCallback();

    /// <summary>
    /// Parse frame with streaming callbacks.
    /// Applies delta decoding using internal state.
    /// </summary>
    public void Parse(
        ReadOnlySpan<byte> data,
        HeaderCallback onHeader,
        KeypointCallback onKeypoint,
        CompleteCallback onComplete);

    /// <summary>
    /// Reset state (next frame treated as master).
    /// </summary>
    public void Reset();
}

public readonly struct KeypointHeader
{
    public ulong FrameId { get; init; }
    public bool IsDelta { get; init; }
    public uint KeypointCount { get; init; }
}

public readonly struct KeypointData
{
    public int Id { get; init; }
    public Point Position { get; init; }  // Absolute position (deltas applied)
    public ushort Confidence { get; init; }
}
```

**Usage in WASM Rendering Loop:**
```csharp
// Reader maintains state between frames
private readonly KeypointReader _reader = new();

void OnFrameReceived(ReadOnlySpan<byte> data)
{
    _reader.Parse(data,
        onHeader: (in KeypointHeader h) =>
        {
            _stage.OnFrameStart(h.FrameId);
            _stage.Clear(_layerId);
        },
        onKeypoint: (in KeypointData kp) =>
        {
            // kp.Position is absolute (reader applied deltas)
            var radius = (int)(kp.Confidence / 10000f * 8) + 3;
            var color = _palette[kp.Id];
            _canvas.DrawCircle(kp.Position.X, kp.Position.Y, radius, color);
        },
        onComplete: () =>
        {
            _stage.OnFrameEnd();
        });
}
```

### KeypointReader - Eager API (Alternative)

```csharp
/// <summary>
/// Stateful reader for keypoint frames (handles master/delta).
/// Returns complete frames - allocates.
/// </summary>
public class KeypointReader
{
    /// <summary>
    /// Decode a keypoint frame, applying deltas to previous state.
    /// </summary>
    public KeypointFrame Decode(ReadOnlySpan<byte> data);

    /// <summary>
    /// Reset state (clear previous keypoints).
    /// </summary>
    public void Reset();
}
```

## Writer API

### Design Decision: Static vs Class Writers

Based on research (Utf8JsonWriter, MessagePackWriter patterns):

- **SegmentationWriter**: Static methods (no state between frames)
- **KeypointWriter**: Class (needs state for master/delta encoding)

### SegmentationWriter - Static Methods (Zero Allocation)

```csharp
namespace RocketWelder.SDK.BinaryProtocols.Segmentation;

/// <summary>
/// Stateless writer for segmentation frames.
/// Static methods write directly to IBufferWriter for zero-copy performance.
/// </summary>
public static class SegmentationWriter
{
    /// <summary>
    /// Write frame header (call once before WriteInstance calls).
    /// </summary>
    public static void WriteHeader(
        IBufferWriter<byte> buffer,
        ulong frameId,
        uint width,
        uint height);

    /// <summary>
    /// Write a single instance with delta-encoded points.
    /// Call multiple times after WriteHeader.
    /// </summary>
    public static void WriteInstance(
        IBufferWriter<byte> buffer,
        byte classId,
        byte instanceId,
        ReadOnlySpan<Point> points);

    /// <summary>
    /// Write a complete frame (header + all instances).
    /// Convenience method for simple cases.
    /// </summary>
    public static void Write(
        IBufferWriter<byte> buffer,
        ulong frameId,
        uint width,
        uint height,
        ReadOnlySpan<SegmentationInstance> instances);
}
```

**Usage:**
```csharp
var buffer = new ArrayBufferWriter<byte>();

// Option 1: Streaming (for large frames or memory-constrained)
SegmentationWriter.WriteHeader(buffer, frameId: 42, width: 1920, height: 1080);
SegmentationWriter.WriteInstance(buffer, classId: 0, instanceId: 1, polygon1Points);
SegmentationWriter.WriteInstance(buffer, classId: 1, instanceId: 0, polygon2Points);

// Option 2: Batch (for convenience)
SegmentationWriter.Write(buffer, frameId, width, height, instances);
```

### KeypointWriter - Class with State

```csharp
namespace RocketWelder.SDK.BinaryProtocols.Keypoints;

/// <summary>
/// Stateful writer for keypoint frames.
/// Manages master/delta frame encoding.
/// Class (not static) because state persists across frames.
/// Reusable via Reset() to avoid allocations (like Utf8JsonWriter).
/// </summary>
public class KeypointWriter
{
    /// <summary>
    /// Master frame interval (default: 300 frames).
    /// Frame 0 is always master, then delta until next master.
    /// </summary>
    public int MasterFrameInterval { get; init; } = 300;

    /// <summary>
    /// Write a keypoint frame (automatically chooses master or delta).
    /// Delta encoding uses previous frame for compression.
    /// </summary>
    public void Write(
        IBufferWriter<byte> buffer,
        ulong frameId,
        ReadOnlySpan<Keypoint> keypoints);

    /// <summary>
    /// Force write a master frame.
    /// </summary>
    public void WriteMaster(
        IBufferWriter<byte> buffer,
        ulong frameId,
        ReadOnlySpan<Keypoint> keypoints);

    /// <summary>
    /// Force write a delta frame (errors if no previous frame).
    /// </summary>
    public void WriteDelta(
        IBufferWriter<byte> buffer,
        ulong frameId,
        ReadOnlySpan<Keypoint> keypoints);

    /// <summary>
    /// Reset state (next frame will be master).
    /// Use to switch to new IBufferWriter or new stream.
    /// </summary>
    public void Reset();
}
```

**Usage:**
```csharp
var writer = new KeypointWriter { MasterFrameInterval = 300 };
var buffer = new ArrayBufferWriter<byte>();

// Frame 1: Automatically master (first frame)
writer.Write(buffer, frameId: 1, keypoints1);

// Frame 2: Automatically delta
writer.Write(buffer, frameId: 2, keypoints2);

// Frame 301: Automatically master (interval reached)
writer.Write(buffer, frameId: 301, keypoints301);

// Force master at any time
writer.WriteMaster(buffer, frameId: 500, keypointsForced);
```

## Protocol Specifications

### Segmentation Frame Format

```
┌────────────────────────────────────────────────────────────────┐
│ HEADER                                                          │
├────────────────────────────────────────────────────────────────┤
│ FrameId     : 8 bytes, little-endian uint64                    │
│ Width       : varint (1-5 bytes)                               │
│ Height      : varint (1-5 bytes)                               │
├────────────────────────────────────────────────────────────────┤
│ INSTANCES (repeated until end of data)                          │
├────────────────────────────────────────────────────────────────┤
│ ClassId     : 1 byte                                            │
│ InstanceId  : 1 byte                                            │
│ PointCount  : varint                                            │
│ Points[0]   : X (zigzag-varint), Y (zigzag-varint) - absolute  │
│ Points[1..] : ΔX (zigzag-varint), ΔY (zigzag-varint) - delta   │
└────────────────────────────────────────────────────────────────┘
```

**Example**:
- Frame with 2 instances
- Instance 1: classId=0, instanceId=1, 3 points at (100,100), (110,105), (105,115)
- Instance 2: classId=1, instanceId=0, 2 points at (200,200), (210,200)

```
08 00 00 00 00 00 00 00   # FrameId = 8
80 07                      # Width = 1920 (varint)
38 04                      # Height = 1080 (varint)
00                         # ClassId = 0
01                         # InstanceId = 1
03                         # PointCount = 3
C8 01                      # X = 100 (zigzag)
C8 01                      # Y = 100 (zigzag)
14                         # ΔX = +10 (zigzag)
0A                         # ΔY = +5 (zigzag)
0B                         # ΔX = -5 (zigzag)
14                         # ΔY = +10 (zigzag)
01                         # ClassId = 1
00                         # InstanceId = 0
02                         # PointCount = 2
90 03                      # X = 200 (zigzag)
90 03                      # Y = 200 (zigzag)
14                         # ΔX = +10 (zigzag)
00                         # ΔY = 0 (zigzag)
```

### Keypoints Frame Format

```
┌────────────────────────────────────────────────────────────────┐
│ HEADER                                                          │
├────────────────────────────────────────────────────────────────┤
│ FrameType   : 1 byte (0x00 = Master, 0x01 = Delta)             │
│ FrameId     : 8 bytes, little-endian uint64                    │
│ KeypointCnt : varint                                            │
├────────────────────────────────────────────────────────────────┤
│ MASTER KEYPOINTS (when FrameType = 0x00)                        │
├────────────────────────────────────────────────────────────────┤
│ Id          : varint                                            │
│ X           : 4 bytes, little-endian int32                     │
│ Y           : 4 bytes, little-endian int32                     │
│ Confidence  : 2 bytes, little-endian uint16 (0-10000)          │
├────────────────────────────────────────────────────────────────┤
│ DELTA KEYPOINTS (when FrameType = 0x01)                         │
├────────────────────────────────────────────────────────────────┤
│ Id          : varint                                            │
│ ΔX          : zigzag-varint                                     │
│ ΔY          : zigzag-varint                                     │
│ ΔConfidence : zigzag-varint                                     │
└────────────────────────────────────────────────────────────────┘
```

**Master Frame Example** (3 keypoints):
```
00                         # FrameType = Master
01 00 00 00 00 00 00 00   # FrameId = 1
03                         # KeypointCount = 3
00                         # Id = 0 (nose)
64 00 00 00               # X = 100
C8 00 00 00               # Y = 200
10 27                      # Confidence = 10000 (100%)
01                         # Id = 1 (left_eye)
50 00 00 00               # X = 80
B4 00 00 00               # Y = 180
D0 07                      # Confidence = 2000 (20%)
...
```

**Delta Frame Example** (from previous master):
```
01                         # FrameType = Delta
02 00 00 00 00 00 00 00   # FrameId = 2
03                         # KeypointCount = 3
00                         # Id = 0 (nose)
04                         # ΔX = +2 (zigzag: 2 → 4)
02                         # ΔY = +1 (zigzag: 1 → 2)
00                         # ΔConfidence = 0
01                         # Id = 1 (left_eye)
03                         # ΔX = -1 (zigzag: -1 → 3)
02                         # ΔY = +1 (zigzag: 1 → 2)
14                         # ΔConfidence = +10 (zigzag: 10 → 20)
...
```

## Varint Encoding

Uses Protocol Buffers-compatible varint encoding:
- 7 bits of data per byte
- High bit (0x80) indicates more bytes follow
- Little-endian byte order

```
Value       Encoded
0           00
1           01
127         7F
128         80 01
16383       FF 7F
16384       80 80 01
```

## ZigZag Encoding

Encodes signed integers as unsigned for efficient varint encoding:
```
Signed      Unsigned (ZigZag)
0           0
-1          1
1           2
-2          3
2           4
...
```

Formula:
- Encode: `(n << 1) ^ (n >> 31)`
- Decode: `(n >> 1) ^ -(n & 1)`

## WASM Compatibility

### Allowed Dependencies
- `System.Buffers` - IBufferWriter<byte>
- `System.Memory` - Span<T>, Memory<T>, ReadOnlySpan<T>
- `System.Drawing.Primitives` - Point struct
- BCL primitives only

### Forbidden Dependencies
- `System.Net.Sockets`
- `nng.NETCore`
- `Emgu.CV`
- `ASP.NET Core`
- Any native interop

## Usage Examples

### Encoding Segmentation

```csharp
using RocketWelder.SDK.BinaryProtocols.Segmentation;

var instances = new[]
{
    new SegmentationInstance
    {
        ClassId = 0,
        InstanceId = 1,
        Points = new Point[] { new(100, 100), new(200, 100), new(150, 200) }
    }
};

var buffer = new ArrayBufferWriter<byte>();
SegmentationWriter.Write(buffer, frameId: 42, width: 1920, height: 1080, instances);
byte[] encoded = buffer.WrittenSpan.ToArray();
```

### Decoding Segmentation

```csharp
using RocketWelder.SDK.BinaryProtocols.Segmentation;

ReadOnlySpan<byte> data = /* from transport */;
var frame = SegmentationReader.Parse(data);

foreach (var instance in frame.Instances.Span)
{
    Console.WriteLine($"Class {instance.ClassId}, Instance {instance.InstanceId}");
    foreach (var point in instance.Points.Span)
    {
        Console.WriteLine($"  Point: ({point.X}, {point.Y})");
    }
}
```

### Encoding Keypoints (Stateful)

```csharp
using RocketWelder.SDK.BinaryProtocols.Keypoints;

var writer = new KeypointWriter { MasterFrameInterval = 300 };

// Frame 1: Master (automatic)
var keypoints1 = new[] { new Keypoint { Id = 0, Position = new(100, 200), Confidence = 9500 } };
var buffer1 = new ArrayBufferWriter<byte>();
writer.Write(buffer1, frameId: 1, keypoints1);  // Master frame

// Frame 2: Delta (automatic)
var keypoints2 = new[] { new Keypoint { Id = 0, Position = new(102, 201), Confidence = 9500 } };
var buffer2 = new ArrayBufferWriter<byte>();
writer.Write(buffer2, frameId: 2, keypoints2);  // Delta frame (+2, +1, 0)
```

### Decoding Keypoints (Stateful)

```csharp
using RocketWelder.SDK.BinaryProtocols.Keypoints;

var reader = new KeypointReader();

// Decode master frame
var frame1 = reader.Decode(masterFrameData);
// frame1.Keypoints contains absolute positions

// Decode delta frame
var frame2 = reader.Decode(deltaFrameData);
// frame2.Keypoints contains reconstructed absolute positions
```

## Round-Trip Testing

All implementations must pass round-trip tests:

```csharp
[Fact]
public void Segmentation_RoundTrip()
{
    var original = new SegmentationFrame
    {
        FrameId = 42,
        Width = 1920,
        Height = 1080,
        Instances = new[]
        {
            new SegmentationInstance
            {
                ClassId = 0,
                InstanceId = 1,
                Points = new Point[] { new(100, 100), new(200, 150), new(150, 200) }
            }
        }
    };

    // Encode
    var buffer = new ArrayBufferWriter<byte>();
    SegmentationWriter.Write(buffer, original.FrameId, original.Width, original.Height, original.Instances.Span);

    // Decode
    var decoded = SegmentationReader.Parse(buffer.WrittenSpan);

    // Assert
    Assert.Equal(original.FrameId, decoded.FrameId);
    Assert.Equal(original.Width, decoded.Width);
    Assert.Equal(original.Height, decoded.Height);
    Assert.Equal(original.Instances.Length, decoded.Instances.Length);
    // ... deep equality checks
}
```

## Migration Path

### WASM Client (rocket-welder2)

Before:
```csharp
// SegmentationDecoder.cs - protocol parsing mixed with rendering
var reader = new BinaryFrameReader(data);
var frameId = reader.ReadUInt64LE();
// ... lots of parsing code ...
canvas.DrawPolygon(points, color);
```

After:
```csharp
// SegmentationDecoder.cs - uses SDK, only rendering
var frame = SegmentationReader.Parse(data);
foreach (var instance in frame.Instances.Span)
{
    var color = _palette[instance.ClassId];
    var skPoints = instance.Points.Span.Select(p => new SKPoint(p.X, p.Y)).ToArray();
    canvas.DrawPolygon(skPoints, color);
}
```

## File Structure

```
RocketWelder.SDK.BinaryProtocols/
├── RocketWelder.SDK.BinaryProtocols.csproj
├── BinaryFrameReader.cs          (existing, rename namespace)
├── BinaryFrameWriter.cs          (NEW)
├── VarintExtensions.cs           (existing, rename namespace)
├── Segmentation/
│   ├── SegmentationFrame.cs
│   ├── SegmentationInstance.cs
│   ├── SegmentationReader.cs
│   └── SegmentationWriter.cs
└── Keypoints/
    ├── Keypoint.cs
    ├── KeypointFrame.cs
    ├── KeypointReader.cs
    └── KeypointWriter.cs
```

## Version History

| Version | Changes |
|---------|---------|
| 1.0.0   | Initial release with Segmentation and Keypoints protocols |
