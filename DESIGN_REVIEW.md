# Design Review: C# Protocol API

**Date:** 2025-12-04
**Status:** ✅ Completed - API Cleanup Done

## Overview

This document reviews the current state of the C# protocol API (KeyPoints and Segmentation) after the transport abstraction refactor. The goal is to ensure consistency, minimize API surface, and maintain good design principles.

---

## 1. Current API Inventory (After Cleanup)

### KeyPoints Protocol (`KeyPointsProtocol.cs`)

| Type | Role | Status |
|------|------|--------|
| `IKeyPointsSink` | Writer factory | ✅ Clean |
| `IKeyPointsWriter` | Per-frame writer | ✅ Good |
| `IKeyPointsSource` | Streaming reader | ✅ Good |
| `KeyPointsSink` | Sink implementation | ✅ Good |
| `KeyPointsSource` | Source implementation | ✅ Good |
| `KeyPointsWriter` | Writer implementation (internal) | ✅ Good |
| `KeyPointsFrame` | Frame data structure | ✅ Good |
| `KeyPoint` | Keypoint data structure | ✅ Renamed |
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
| `SegmentationResultWriter` | Writer implementation | ✅ Fixed - uses StreamFrameSink |
| `SegmentationFrame` | Frame data structure | ✅ Good |
| `SegmentationInstance` | Instance data | ✅ Renamed from SegmentationInstanceData |
| `ISegmentationResultStorage` | OLD factory interface | ✅ Marked [Obsolete] |

### Removed Types
- ❌ `ISegmentationResultReader` - Removed (use `ISegmentationResultSource`)
- ❌ `SegmentationResultReader` - Removed (use `SegmentationResultSource`)
- ❌ `SegmentationInstance` (ref struct) - Removed (use heap `SegmentationInstance`)
- ❌ `SegmentationFrameMetadata` - Removed (use `SegmentationFrame` properties)
- ❌ `RawStreamSink` - Removed (all use `StreamFrameSink` consistently)
- ❌ `IKeyPointsSink.Read()` - Removed (use `KeyPointsSource`)

---

## 2. Issues Resolved

### 2.1 Single Responsibility Violation ✅ FIXED

**Before:**
```csharp
public interface IKeyPointsSink : IDisposable, IAsyncDisposable
{
    IKeyPointsWriter CreateWriter(ulong frameId);  // ✅ Writing
    Task<KeyPointsSeries> Read(...);               // ❌ Reading!
}
```

**After:**
```csharp
public interface IKeyPointsSink : IDisposable, IAsyncDisposable
{
    IKeyPointsWriter CreateWriter(ulong frameId);  // ✅ Writing only
}

// Reading is done via separate Source:
public interface IKeyPointsSource : IDisposable, IAsyncDisposable
{
    IAsyncEnumerable<KeyPointsFrame> ReadFramesAsync(CancellationToken ct = default);
}
```

---

### 2.2 Duplicate/Redundant Types ✅ REMOVED

| Redundant Type | Action | Status |
|----------------|--------|--------|
| `ISegmentationResultReader` | Removed | ✅ Done |
| `SegmentationResultReader` | Removed | ✅ Done |
| `SegmentationInstance` (ref struct) | Removed | ✅ Done |
| `SegmentationFrameMetadata` | Removed | ✅ Done |
| `RawStreamSink` | Removed | ✅ Done |
| `ISegmentationResultStorage` | Marked `[Obsolete]` | ✅ Done |

---

### 2.3 API Symmetry ✅ ACHIEVED

| Aspect | KeyPoints | Segmentation | Consistent? |
|--------|-----------|--------------|-------------|
| Sink interface | `IKeyPointsSink` | `ISegmentationResultSink` | ✅ |
| Source interface | `IKeyPointsSource` | `ISegmentationResultSource` | ✅ |
| Writer interface | `IKeyPointsWriter` | `ISegmentationResultWriter` | ✅ |
| Read on Sink? | NO | NO | ✅ |
| Old Reader class? | NO | NO | ✅ |
| Old Storage deprecated? | YES | YES | ✅ |
| Frame struct | `KeyPointsFrame` | `SegmentationFrame` | ✅ |
| Data struct | `KeyPoint` | `SegmentationInstance` | ✅ |
| Stream framing | `StreamFrameSink` | `StreamFrameSink` | ✅ |

---

### 2.4 Naming Consistency ✅ FIXED

| Before | After | Status |
|--------|-------|--------|
| `KeyPointData` | `KeyPoint` | ✅ Renamed |
| `SegmentationInstanceData` | `SegmentationInstance` | ✅ Renamed |

---

### 2.5 Stream Constructor Consistency ✅ FIXED

**Before:** Inconsistent - KeyPointsSink used framing, SegmentationResultWriter did not.

**After:** Both use `StreamFrameSink` with varint length-prefix framing:
```csharp
// Both protocols now consistent:
public KeyPointsSink(Stream stream, ...)
    : this(new StreamFrameSink(stream, leaveOpen), ...)

public SegmentationResultWriter(ulong frameId, uint width, uint height, Stream destination, bool leaveOpen = false)
{
    _frameSink = new StreamFrameSink(destination, leaveOpen);  // Consistent!
}
```

---

## 3. Final API

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
public readonly struct KeyPoint { ... }

// Implementations
public class KeyPointsSink : IKeyPointsSink { ... }
public class KeyPointsSource : IKeyPointsSource { ... }
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
public readonly struct SegmentationInstance { ... }

// Implementations
public class SegmentationResultSink : ISegmentationResultSink { ... }
public class SegmentationResultSource : ISegmentationResultSource { ... }
```

---

## 4. Summary

| Issue | Severity | Status |
|-------|----------|--------|
| `IKeyPointsSink.Read()` violates SRP | High | ✅ Fixed |
| Duplicate `SegmentationResultReader` | High | ✅ Removed |
| Duplicate `SegmentationInstance` types | Medium | ✅ Removed |
| `ISegmentationResultStorage` not deprecated | Low | ✅ Fixed |
| Stream constructor inconsistency | Medium | ✅ Fixed |
| Naming inconsistency (`KeyPointData`) | Low | ✅ Fixed |
| File organization | Low | Future |
| Performance: `ToArray()` allocation | Low | Future |

---

## 5. Remaining Work

### File Reorganization (Future/Optional)
Extract segmentation types from `RocketWelderClient.cs` to `SegmentationProtocol.cs` for better discoverability:

```
KeyPointsProtocol.cs       → KeyPoints types
SegmentationProtocol.cs    → Segmentation types (extract)
VarintExtensions.cs        → Varint utilities (extract)
RocketWelderClient.cs      → Client and controller types only
```

### Performance Optimizations (Future)
- Parse directly from `ReadOnlySpan<byte>` instead of `ToArray()`
- Use `ArrayPool<T>` for high-throughput scenarios

### Python SDK Update
Python SDK needs to be updated to use varint length-prefix framing to match C#.
