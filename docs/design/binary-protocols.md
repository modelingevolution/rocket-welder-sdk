# RocketWelder.BinaryProtocol Design Document

## Problem Statement

We need to test **round-trip encoding/decoding** cross-platform:
- **SDK** (Linux container) encodes ML results using `SegmentationResultWriter`, `KeyPointsWriter`
- **Client** (WASM browser) decodes using `SegmentationDecoder`, `KeypointsDecoder`

Currently, we **cannot** test this because:
1. SDK writers are coupled to transport (`IFrameSink`, `Stream`)
2. Client decoders are coupled to rendering (`IStage`, `ICanvas`)

## Solution

Extract **pure protocol encoding/decoding** into `RocketWelder.BinaryProtocol`:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                 RocketWelder.BinaryProtocol                              │
│                 (WASM Compatible, No Transport, No Rendering)            │
├─────────────────────────────────────────────────────────────────────────┤
│  Low-Level Primitives (EXISTS)                                           │
│  ├── BinaryFrameReader              ReadOnlySpan<byte> → primitives     │
│  └── VarintExtensions               Varint/ZigZag helpers               │
├─────────────────────────────────────────────────────────────────────────┤
│  Low-Level Primitives (NEW)                                              │
│  └── BinaryFrameWriter              primitives → Span<byte>             │
├─────────────────────────────────────────────────────────────────────────┤
│  Protocol Abstractions (NEW) - Pure encode/decode, no transport          │
│  ├── SegmentationProtocol           Write/Read frame structure          │
│  │   ├── SegmentationFrame          Header + instances                  │
│  │   └── SegmentationInstance       ClassId, InstanceId, Points[]       │
│  └── KeypointsProtocol              Write/Read frame structure          │
│      ├── KeypointsFrame             Header + keypoints                  │
│      └── Keypoint                   Id, Position, Confidence            │
└─────────────────────────────────────────────────────────────────────────┘
```

## How This Enables Round-Trip Testing

```csharp
// TEST: SDK encoding → BinaryProtocol decoding
[Fact]
public void Segmentation_RoundTrip()
{
    // 1. SDK writes to MemoryStream (simulates IFrameSink)
    using var stream = new MemoryStream();
    using var writer = new SegmentationResultWriter(frameId: 42, width: 1920, height: 1080, stream);
    writer.AddInstance(classId: 0, instanceId: 1, points);
    writer.Commit();

    // 2. Extract raw bytes (skip length prefix from framing)
    var bytes = ExtractFrameBytes(stream);

    // 3. Decode using BinaryProtocol (WASM-compatible)
    var frame = SegmentationProtocol.Read(bytes);

    // 4. Assert round-trip
    Assert.Equal(42UL, frame.FrameId);
    Assert.Equal(1920U, frame.Width);
    Assert.Single(frame.Instances);
    Assert.Equal(0, frame.Instances[0].ClassId);
}
```

## What Exists vs What's New

### Exists in RocketWelder.SDK

```csharp
// SegmentationResultWriter - writes to IFrameSink/Stream
class SegmentationResultWriter : ISegmentationResultWriter
{
    public void AddInstance(byte classId, byte instanceId, ReadOnlySpan<Point> points);
    public void Commit();  // Writes to transport with length-prefix framing
}

// KeyPointsWriter - writes to IFrameSink
internal class KeyPointsWriter : IKeyPointsWriter
{
    public void Append(int keypointId, int x, int y, float confidence);
    public void Dispose();  // Writes frame on dispose
}
```

### Exists in RocketWelder.BinaryProtocol

```csharp
// BinaryFrameReader - low-level reading
public ref struct BinaryFrameReader
{
    public ulong ReadUInt64LE();
    public uint ReadVarint();
    public int ReadZigZagVarint();
    // ...
}

// VarintExtensions - encoding helpers
public static class VarintExtensions
{
    public static void WriteVarint(this Stream stream, uint value);
    public static uint ZigZagEncode(this int value);
    // ...
}
```

### Exists in rocket-welder2 (decoding + rendering MIXED)

```csharp
// SegmentationDecoder - decodes AND renders
public class SegmentationDecoder : IFrameDecoder
{
    public DecodeResultV2 Decode(ReadOnlySpan<byte> data)
    {
        var reader = new BinaryFrameReader(data);
        // Parse header
        var frameId = reader.ReadUInt64LE();
        // ... parse instances ...
        // RENDER to canvas (coupled!)
        canvas.DrawPolygon(points.ToArray(), color);
    }
}
```

### NEW in RocketWelder.BinaryProtocol

```csharp
// BinaryFrameWriter - symmetric to BinaryFrameReader
public ref struct BinaryFrameWriter
{
    public BinaryFrameWriter(Span<byte> buffer);
    public void WriteUInt64LE(ulong value);
    public void WriteVarint(uint value);
    public void WriteZigZagVarint(int value);
    // ...
}

// SegmentationProtocol - pure protocol, no transport, no rendering
public static class SegmentationProtocol
{
    // WRITE: Encode frame to bytes
    public static int Write(Span<byte> buffer, in SegmentationFrame frame);
    public static int WriteHeader(Span<byte> buffer, ulong frameId, uint width, uint height);
    public static int WriteInstance(Span<byte> buffer, byte classId, byte instanceId,
                                    ReadOnlySpan<Point> points);

    // READ: Decode bytes to frame
    public static SegmentationFrame Read(ReadOnlySpan<byte> data);
    public static bool TryRead(ReadOnlySpan<byte> data, out SegmentationFrame frame);
}

// Data structures (WASM-compatible, System.Drawing.Point is supported)
public readonly struct SegmentationFrame
{
    public ulong FrameId { get; init; }
    public uint Width { get; init; }
    public uint Height { get; init; }
    public SegmentationInstance[] Instances { get; init; }
}

public readonly struct SegmentationInstance
{
    public byte ClassId { get; init; }
    public byte InstanceId { get; init; }
    public Point[] Points { get; init; }
}

// KeypointsProtocol - pure protocol
public static class KeypointsProtocol
{
    public static int WriteMasterFrame(Span<byte> buffer, ulong frameId,
                                       ReadOnlySpan<Keypoint> keypoints);
    public static int WriteDeltaFrame(Span<byte> buffer, ulong frameId,
                                      ReadOnlySpan<Keypoint> current,
                                      ReadOnlySpan<Keypoint> previous);
    public static KeypointsFrame Read(ReadOnlySpan<byte> data);
}

public readonly struct KeypointsFrame
{
    public ulong FrameId { get; init; }
    public bool IsMasterFrame { get; init; }
    public Keypoint[] Keypoints { get; init; }
}

public readonly struct Keypoint
{
    public int Id { get; init; }
    public Point Position { get; init; }
    public ushort Confidence { get; init; }
}
```

## Integration Points

### SDK Uses BinaryProtocol for Encoding

```csharp
// In RocketWelder.SDK - SegmentationResultWriter refactored to use BinaryProtocol
class SegmentationResultWriter
{
    private void WriteInstance(byte classId, byte instanceId, ReadOnlySpan<Point> points)
    {
        var instanceSize = SegmentationProtocol.CalculateInstanceSize(points.Length);
        var buffer = _memoryPool.Rent(instanceSize);

        // Use BinaryProtocol for encoding (pure protocol, no transport)
        var written = SegmentationProtocol.WriteInstance(buffer.Span, classId, instanceId, points);

        // Then write to transport
        _buffer.Write(buffer.Span[..written]);
    }
}
```

### Client Decoders Use BinaryProtocol for Decoding

```csharp
// In rocket-welder2 - SegmentationDecoder refactored
public class SegmentationDecoder : IFrameDecoder
{
    public DecodeResultV2 Decode(ReadOnlySpan<byte> data)
    {
        // Use BinaryProtocol for decoding (pure protocol)
        var frame = SegmentationProtocol.Read(data);

        _stage.OnFrameStart(frame.FrameId);
        _stage.Clear(_layerId);
        var canvas = _stage[_layerId];

        // Rendering logic stays here
        foreach (var instance in frame.Instances)
        {
            var color = _palette[instance.ClassId];
            var skPoints = instance.Points.Select(p => new SKPoint(p.X, p.Y)).ToArray();
            canvas.DrawPolygon(skPoints, color, thickness: 2);
        }

        _stage.OnFrameEnd();
        return DecodeResultV2.Ok(data.Length, frame.FrameId, layerCount: 1);
    }
}
```

## Protocol Specifications

### Segmentation Frame Format
```
[FrameId: 8 bytes, little-endian uint64]
[Width: varint]
[Height: varint]
[Instances...]

Instance:
[ClassId: 1 byte]
[InstanceId: 1 byte]
[PointCount: varint]
[Point0: X zigzag-varint, Y zigzag-varint]  (absolute)
[Point1+: deltaX zigzag-varint, deltaY zigzag-varint]
```

### Keypoints Frame Format
```
[FrameType: 1 byte (0x00=Master, 0x01=Delta)]
[FrameId: 8 bytes, little-endian uint64]
[KeypointCount: varint]

Master Keypoint:
[Id: varint]
[X: 4 bytes, int32 LE]
[Y: 4 bytes, int32 LE]
[Confidence: 2 bytes, uint16 LE]

Delta Keypoint:
[Id: varint]
[DeltaX: zigzag-varint]
[DeltaY: zigzag-varint]
[DeltaConfidence: zigzag-varint]
```

## File Structure

```
RocketWelder.BinaryProtocol/
├── RocketWelder.BinaryProtocol.csproj
├── BinaryFrameReader.cs          (EXISTS)
├── BinaryFrameWriter.cs          (NEW)
├── VarintExtensions.cs           (EXISTS)
├── SegmentationProtocol.cs       (NEW)
├── SegmentationFrame.cs          (NEW)
├── SegmentationInstance.cs       (NEW)
├── KeypointsProtocol.cs          (NEW)
├── KeypointsFrame.cs             (NEW)
└── Keypoint.cs                   (NEW)
```

## WASM Compatibility

**Allowed:**
- `System.Drawing.Point` (supported in WASM)
- `Span<byte>`, `ReadOnlySpan<byte>`
- BCL primitives

**Forbidden:**
- `System.Net.Sockets`
- `nng.NETCore`
- `ASP.NET Core`
- Any transport dependencies

## Implementation Phases

### Phase 1: Add BinaryFrameWriter
- Symmetric to BinaryFrameReader
- Same methods for writing primitives

### Phase 2: Add Protocol Abstractions
- `SegmentationProtocol` with `Read()` and `Write()` methods
- `KeypointsProtocol` with `Read()` and `Write()` methods
- Data structures: `SegmentationFrame`, `SegmentationInstance`, `KeypointsFrame`, `Keypoint`

### Phase 3: Update SDK
- Refactor `SegmentationResultWriter` to use `SegmentationProtocol.WriteInstance()`
- Refactor `KeyPointsWriter` to use `KeypointsProtocol.WriteMasterFrame()/WriteDeltaFrame()`

### Phase 4: Update rocket-welder2 Decoders
- Refactor `SegmentationDecoder` to use `SegmentationProtocol.Read()`
- Refactor `KeypointsDecoder` to use `KeypointsProtocol.Read()`

### Phase 5: Add Round-Trip Tests
- Test SDK encode → BinaryProtocol decode
- Test BinaryProtocol encode → BinaryProtocol decode
