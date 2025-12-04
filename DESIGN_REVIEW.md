# Design Review: C# Protocol API

**Date:** 2025-12-04
**Status:** Issues Identified - Pending Refactoring

## Overview

This document reviews the current state of the C# protocol API (KeyPoints and Segmentation) after the transport abstraction refactor. The goal is to ensure consistency, minimize API surface, and maintain good design principles.

---

## 1. Current API Inventory

### KeyPoints Protocol (`KeyPointsProtocol.cs`)

| Type | Role | Status |
|------|------|--------|
| `IKeyPointsSink` | Writer factory + Read method | ⚠️ Violates SRP |
| `IKeyPointsWriter` | Per-frame writer | ✅ Good |
| `IKeyPointsSource` | Streaming reader | ✅ Good |
| `KeyPointsSink` | Sink implementation | ✅ Good |
| `KeyPointsSource` | Source implementation | ✅ Good |
| `KeyPointsWriter` | Writer implementation (internal) | ✅ Good |
| `KeyPointsFrame` | Frame data structure | ✅ Good |
| `KeyPointData` | Keypoint data structure | ⚠️ Naming inconsistent |
| `KeyPointsSeries` | In-memory query helper | ✅ Good (batch use-case) |
| `IKeyPointsStorage` | Legacy alias | ✅ Deprecated |
| `FileKeyPointsStorage` | Legacy alias | ✅ Deprecated |

### Segmentation Protocol (`RocketWelderClient.cs`)

| Type | Role | Status |
|------|------|--------|
| `ISegmentationResultSink` | Writer factory | ✅ Good |
| `ISegmentationResultWriter` | Per-frame writer | ✅ Good |
| `ISegmentationResultSource` | Streaming reader | ✅ Good |
| `SegmentationResultSink` | Sink implementation | ✅ Good |
| `SegmentationResultSource` | Source implementation | ✅ Good |
| `SegmentationResultWriter` | Writer implementation | ⚠️ Inconsistent Stream ctor |
| `SegmentationFrame` | Frame data structure | ✅ Good |
| `SegmentationInstanceData` | Instance data (heap) | ✅ Good |
| `ISegmentationResultReader` | OLD single-frame reader | ❌ Remove |
| `SegmentationResultReader` | OLD reader implementation | ❌ Remove |
| `SegmentationInstance` | OLD ref struct | ❌ Remove |
| `ISegmentationResultStorage` | OLD factory interface | ❌ Deprecate |
| `SegmentationFrameMetadata` | Header struct | ⚠️ Redundant with SegmentationFrame |

---

## 2. Issues Identified

### 2.1 Single Responsibility Violation

**Problem:** `IKeyPointsSink` has a `Read()` method.

```csharp
public interface IKeyPointsSink : IDisposable, IAsyncDisposable
{
    IKeyPointsWriter CreateWriter(ulong frameId);  // ✅ Writing
    Task<KeyPointsSeries> Read(...);               // ❌ Reading!
}
```

A **Sink** should only write. Reading should be done via `IKeyPointsSource`.

**Fix:** Remove `Read()` from `IKeyPointsSink`.

---

### 2.2 Duplicate/Redundant Types

| Redundant Type | Should Use Instead | Action |
|----------------|-------------------|--------|
| `ISegmentationResultReader` | `ISegmentationResultSource` | Remove |
| `SegmentationResultReader` | `SegmentationResultSource` | Remove |
| `SegmentationInstance` (ref struct) | `SegmentationInstanceData` | Remove |
| `ISegmentationResultStorage` | `ISegmentationResultSink` | Deprecate |
| `SegmentationFrameMetadata` | `SegmentationFrame` properties | Consider removing |

The old reader classes (`SegmentationResultReader`, `ISegmentationResultReader`) don't use the transport abstraction and are incompatible with `IFrameSource`. They should be removed.

---

### 2.3 API Asymmetry

| Aspect | KeyPoints | Segmentation | Consistent? |
|--------|-----------|--------------|-------------|
| Sink interface | `IKeyPointsSink` | `ISegmentationResultSink` | ✅ |
| Source interface | `IKeyPointsSource` | `ISegmentationResultSource` | ✅ |
| Writer interface | `IKeyPointsWriter` | `ISegmentationResultWriter` | ✅ |
| Read on Sink? | YES | NO | ❌ |
| Old Reader class? | NO | YES | ❌ |
| Old Storage deprecated? | YES | NO | ❌ |
| Frame struct | `KeyPointsFrame` | `SegmentationFrame` | ✅ |
| Data struct | `KeyPointData` | `SegmentationInstanceData` | ⚠️ |

---

### 2.4 Naming Inconsistencies

| Current | Suggested | Reason |
|---------|-----------|--------|
| `KeyPointData` | `KeyPoint` | Simpler, matches `SegmentationInstance` pattern |
| `SegmentationInstanceData` | `SegmentationInstance` | Remove "Data" suffix after removing ref struct |

---

### 2.5 Stream Constructor Inconsistency

```csharp
// KeyPointsSink - wraps in StreamFrameSink (WITH length-prefix framing)
public KeyPointsSink(Stream stream, ...)
    : this(new StreamFrameSink(stream, leaveOpen), ...)

// SegmentationResultWriter - wraps in RawStreamSink (WITHOUT framing)
public SegmentationResultWriter(..., Stream destination)
{
    _frameSink = new RawStreamSink(destination);
}
```

This is inconsistent and confusing. Users must know implementation details.

**Options:**
- **A)** Both use `RawStreamSink` (no framing) - backward compatible
- **B)** Both use `StreamFrameSink` (with framing) - consistent but breaking

**Recommendation:** Document clearly which constructor uses framing.

---

### 2.6 File Organization

**Current:**
- `KeyPointsProtocol.cs` - KeyPoints types only
- `RocketWelderClient.cs` - Segmentation + Client + Controllers + Varint utilities (800+ lines)

**Problems:**
- Hard to discover segmentation protocol types
- Varint utilities buried in unrelated file
- `RocketWelderClient.cs` violates Single Responsibility

**Recommended:**
```
KeyPointsProtocol.cs       → KeyPoints types
SegmentationProtocol.cs    → Segmentation types (extract)
VarintExtensions.cs        → Varint utilities (extract)
RocketWelderClient.cs      → Client and controller types only
```

---

## 3. Performance Analysis

### 3.1 Good Patterns ✅

- **Buffered atomic writes:** Writers buffer to `MemoryStream`, write atomically on dispose
- **`IAsyncEnumerable` streaming:** Enables backpressure and memory-efficient processing
- **Delta compression:** KeyPoints protocol uses master/delta frames for bandwidth reduction
- **Varint encoding:** Variable-length integers reduce message size

### 3.2 Concerns ⚠️

#### Allocation in Source parsing

```csharp
private SegmentationFrame ParseFrame(ReadOnlyMemory<byte> frameData)
{
    using var stream = new MemoryStream(frameData.ToArray());  // Allocation!
```

Every frame causes an array copy. Could parse directly from `ReadOnlySpan<byte>`.

#### List allocations per frame

```csharp
var keypoints = new List<KeyPointData>((int)keypointCount);  // Allocation
var instances = new List<SegmentationInstanceData>();         // Allocation
```

For high-throughput (30+ fps), consider `ArrayPool<T>` or buffer reuse.

#### Removed zero-allocation reader

The old `SegmentationResultReader` used `MemoryPool<Point>` for zero-allocation reads. The new `SegmentationResultSource` allocates `Point[]` per instance.

**Trade-off:** Simpler API vs. performance. Acceptable for most use-cases.

---

## 4. Recommended Changes

### Priority 1: Remove Redundant Types

```csharp
// DELETE these types:
- ISegmentationResultReader
- SegmentationResultReader
- SegmentationInstance (ref struct version)
- RawStreamSink (if not needed after cleanup)

// ADD [Obsolete] attribute:
- ISegmentationResultStorage
```

### Priority 2: Fix SRP Violation

```csharp
// REMOVE from IKeyPointsSink:
Task<KeyPointsSeries> Read(string json, IFrameSource frameSource);

// Use KeyPointsSource instead for reading
```

### Priority 3: Consistent Naming

```csharp
// Rename:
KeyPointData → KeyPoint
```

### Priority 4: Document Stream Behavior

Add XML docs clarifying:
- `KeyPointsSink(Stream)` uses length-prefix framing
- `SegmentationResultWriter(Stream)` does NOT use framing (backward compat)

### Priority 5: File Reorganization (Future)

Extract segmentation types to `SegmentationProtocol.cs` for better discoverability.

---

## 5. Target API (After Cleanup)

### KeyPoints Protocol

```csharp
// Interfaces
public interface IKeyPointsSink : IDisposable, IAsyncDisposable
{
    IKeyPointsWriter CreateWriter(ulong frameId);
}

public interface IKeyPointsSource : IDisposable, IAsyncDisposable
{
    IAsyncEnumerable<KeyPointsFrame> ReadFramesAsync(CancellationToken ct = default);
}

public interface IKeyPointsWriter : IDisposable, IAsyncDisposable
{
    void Append(int keypointId, int x, int y, float confidence);
    void Append(int keypointId, Point p, float confidence);
    Task AppendAsync(int keypointId, int x, int y, float confidence);
    Task AppendAsync(int keypointId, Point p, float confidence);
}

// Data structures
public readonly struct KeyPointsFrame { ... }
public readonly struct KeyPoint { ... }  // Renamed from KeyPointData

// Implementations
public class KeyPointsSink : IKeyPointsSink { ... }
public class KeyPointsSource : IKeyPointsSource { ... }

// Optional: batch query helper
public class KeyPointsSeries { ... }
```

### Segmentation Protocol

```csharp
// Interfaces
public interface ISegmentationResultSink : IDisposable, IAsyncDisposable
{
    ISegmentationResultWriter CreateWriter(ulong frameId, uint width, uint height);
}

public interface ISegmentationResultSource : IDisposable, IAsyncDisposable
{
    IAsyncEnumerable<SegmentationFrame> ReadFramesAsync(CancellationToken ct = default);
}

public interface ISegmentationResultWriter : IDisposable, IAsyncDisposable
{
    void Append(byte classId, byte instanceId, in ReadOnlySpan<Point> points);
    void Append(byte classId, byte instanceId, Point[] points);
    // ... other overloads
}

// Data structures
public readonly struct SegmentationFrame { ... }
public readonly struct SegmentationInstance { ... }  // Renamed from SegmentationInstanceData

// Implementations
public class SegmentationResultSink : ISegmentationResultSink { ... }
public class SegmentationResultSource : ISegmentationResultSource { ... }
```

---

## 6. Summary

| Issue | Severity | Status |
|-------|----------|--------|
| `IKeyPointsSink.Read()` violates SRP | High | Pending |
| Duplicate `SegmentationResultReader` | High | Pending |
| Duplicate `SegmentationInstance` types | Medium | Pending |
| `ISegmentationResultStorage` not deprecated | Low | Pending |
| Stream constructor inconsistency | Medium | Document |
| Naming inconsistency (`KeyPointData`) | Low | Pending |
| File organization | Low | Future |
| Performance: `ToArray()` allocation | Low | Future |

---

## 7. Next Steps

1. Get approval on this design review
2. Implement Priority 1-3 changes
3. Update tests
4. Update documentation
5. Consider Priority 4-5 for future iterations
