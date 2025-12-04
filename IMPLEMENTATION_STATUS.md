# Implementation Status: Transport Abstraction Refactor

## Overview

This document tracks the progress of refactoring from `IKeyPointsStorage`/`ISegmentationResultStorage` to the new Sink/Source pattern with transport abstraction.

### Design Goals

1. **Sink** = Writer factory (creates per-frame writers, uses `IFrameSink`)
2. **Source** = Streaming reader (yields frames via `IAsyncEnumerable`, uses `IFrameSource`)
3. **Transport** = Frame boundary handling (length-prefix for streams, native for WebSocket/NNG)

---

## Current Status Summary

| Component | Status | Notes |
|-----------|--------|-------|
| **C# Transport Layer** | ✅ 100% | All transports implemented (Stream, TCP, Unix Socket, WebSocket, NNG) |
| **C# KeyPoints Protocol** | ⏳ 50% | Sink done, Source not implemented |
| **C# Segmentation Protocol** | ⏳ 30% | Writer has bug, Source not implemented |
| **Python Transport Layer** | ✅ 67% | 4/6 transports working |
| **Python KeyPoints Protocol** | ⏳ 50% | Sink done, Source not implemented |
| **Python Segmentation Protocol** | ⏳ 50% | Writer done, Source not implemented |
| **Tests** | ⏳ Partial | 48 transport tests pass, some protocol tests failing |

---

## C# Implementation

### Transport Layer ✅

| File | Status | Notes |
|------|--------|-------|
| `Transport/IFrameSink.cs` | ✅ | Interface complete |
| `Transport/IFrameSource.cs` | ✅ | Interface complete |
| `Transport/StreamFrameSink.cs` | ✅ | Varint length-prefix framing |
| `Transport/StreamFrameSource.cs` | ✅ | Varint length-prefix framing |
| `Transport/TcpFrameSink.cs` | ✅ | 4-byte LE length-prefix |
| `Transport/TcpFrameSource.cs` | ✅ | 4-byte LE length-prefix |
| `Transport/UnixSocketFrameSink.cs` | ✅ | Unix domain socket support |
| `Transport/UnixSocketFrameSource.cs` | ✅ | Unix domain socket support |
| `Transport/WebSocketFrameSink.cs` | ✅ | Native message boundaries |
| `Transport/WebSocketFrameSource.cs` | ✅ | Native message boundaries |
| `Transport/NngFrameSink.cs` | ✅ | NNG Pub/Sub and Push/Pull patterns |
| `Transport/NngFrameSource.cs` | ✅ | NNG Pub/Sub and Push/Pull patterns |

#### NNG Transport Details

Uses `ModelingEvolution.Nng` v1.0.2 package (fork of nng.NETCore).

**Supported Patterns:**
- **Push/Pull** - Reliable point-to-point with load balancing (recommended)
- **Pub/Sub** - One-to-many broadcast (has slow subscriber limitation)

**Features:**
- Pipe notifications for subscriber connection tracking
- `WaitForSubscriberAsync()` for pub/sub synchronization
- Both IPC (`ipc:///tmp/...`) and TCP (`tcp://127.0.0.1:...`) transports

**Usage:**
```csharp
// Push/Pull (reliable)
var pusher = NngFrameSink.CreatePusher("tcp://127.0.0.1:5555");
var puller = NngFrameSource.CreatePuller("tcp://127.0.0.1:5555", bindMode: false);

// Pub/Sub (broadcast)
var publisher = NngFrameSink.CreatePublisher("ipc:///tmp/topic");
var subscriber = NngFrameSource.CreateSubscriber("ipc:///tmp/topic");
await publisher.WaitForSubscriberAsync(TimeSpan.FromSeconds(5));
```

### KeyPoints Protocol ⏳

| Component | Status | Notes |
|-----------|--------|-------|
| `IKeyPointsSink` | ✅ | Interface defined |
| `KeyPointsSink` | ✅ | Uses `IFrameSink`, manages delta state |
| `KeyPointsWriter` | ✅ | Buffers to memory, writes atomically |
| `IKeyPointsSource` | ❌ | **NOT IMPLEMENTED** |
| `KeyPointsSource` | ❌ | **NOT IMPLEMENTED** - needs `IAsyncEnumerable` |
| `KeyPointsFrame` | ❌ | **NOT IMPLEMENTED** |
| `KeyPoint` struct | ❌ | **NOT IMPLEMENTED** |

**Current reader**: `KeyPointsSeries` loads ALL frames into memory - doesn't support streaming.

### Segmentation Protocol ⏳

| Component | Status | Notes |
|-----------|--------|-------|
| `ISegmentationResultSink` | ❌ | **NOT IMPLEMENTED** |
| `SegmentationResultSink` | ❌ | **NOT IMPLEMENTED** |
| `SegmentationResultWriter` | ⚠️ | Has bug - wraps Stream in StreamFrameSink but reader doesn't unwrap |
| `ISegmentationResultSource` | ❌ | **NOT IMPLEMENTED** |
| `SegmentationResultSource` | ❌ | **NOT IMPLEMENTED** - needs `IAsyncEnumerable` |
| `SegmentationFrame` | ❌ | **NOT IMPLEMENTED** |
| `SegmentationInstance` | ⚠️ | Exists but needs update for new pattern |

**Current reader**: `SegmentationResultReader` reads raw stream without using `IFrameSource` - causes data corruption when paired with writer.

### Test Status ❌

**20 tests failing** (70 passed, 20 failed, 1 skipped)

Key failures:
- `RoundTrip_SingleInstance_PreservesData` - Writer/reader mismatch
- `RoundTrip_LargeContour_PreservesData` - Data corruption
- `Reader_EachInstanceGetsOwnBuffer` - Wrong values read
- Multiple `ToNormalized_*` tests - Incorrect parsing

**Root cause**: `SegmentationResultWriter(Stream)` wraps in `StreamFrameSink` (adds varint length prefix), but `SegmentationResultReader(Stream)` reads raw stream (expects no prefix).

---

## Python Implementation

### Transport Layer ✅

| File | Status | Notes |
|------|--------|-------|
| `transport/frame_sink.py` | ✅ | ABC with context manager |
| `transport/frame_source.py` | ✅ | ABC with context manager |
| `transport/stream_transport.py` | ✅ | Varint length-prefix framing |
| `transport/tcp_transport.py` | ✅ | 4-byte LE length-prefix |
| `transport/websocket_transport.py` | ❌ | Not implemented |
| `transport/nng_transport.py` | ❌ | Not implemented |

### KeyPoints Protocol ⏳

| Component | Status | Notes |
|-----------|--------|-------|
| `IKeyPointsSink` | ✅ | ABC defined |
| `KeyPointsSink` | ✅ | Uses `IFrameSink` |
| `KeyPointsWriter` | ✅ | Buffers to BytesIO, writes atomically |
| `IKeyPointsSource` | ❌ | **NOT IMPLEMENTED** |
| `KeyPointsSource` | ❌ | **NOT IMPLEMENTED** - needs async generator |

### Segmentation Protocol ⏳

| Component | Status | Notes |
|-----------|--------|-------|
| `SegmentationResultWriter` | ✅ | Uses `IFrameSink` |
| `SegmentationResultSource` | ❌ | **NOT IMPLEMENTED** - needs async generator |

### Test Status ❌

**Cannot run tests** - missing `posix_ipc` dependency required by `zerobuffer` on Linux.

```
ImportError: posix_ipc is required on Linux. Install with: pip install posix-ipc
```

---

## What Needs To Be Done

### Priority 1: Fix C# Segmentation Writer/Reader Mismatch

The immediate bug: writer and reader are incompatible.

**Option A**: Make `SegmentationResultWriter(Stream)` NOT wrap in StreamFrameSink
- Preserves backward compatibility for direct stream usage
- Transport abstraction only used when explicitly passing `IFrameSink`

**Option B**: Implement `SegmentationResultSource` properly
- Accept `IFrameSource` instead of raw `Stream`
- Return `IAsyncEnumerable<SegmentationFrame>`
- Update tests to use new pattern

**Recommended**: Option B - align with the target architecture.

### Priority 2: Implement Streaming Readers (Source classes)

Both protocols need `IAsyncEnumerable`-based readers:

```csharp
// KeyPoints
public interface IKeyPointsSource : IDisposable, IAsyncDisposable
{
    IAsyncEnumerable<KeyPointsFrame> ReadFramesAsync(CancellationToken ct = default);
}

// Segmentation
public interface ISegmentationResultSource : IDisposable, IAsyncDisposable
{
    IAsyncEnumerable<SegmentationFrame> ReadFramesAsync(CancellationToken ct = default);
}
```

### Priority 3: Python Source Implementations

Same pattern in Python using async generators:

```python
class KeyPointsSource(IKeyPointsSource):
    async def read_frames_async(self) -> AsyncIterator[KeyPointsFrame]:
        while True:
            frame_data = await self._frame_source.read_frame_async()
            if not frame_data:
                return
            yield self._parse_frame(frame_data)
```

### Priority 4: Fix Python Test Dependencies

Add `posix-ipc` to dependencies or make it optional.

### Priority 5: Update Tests

- Update existing tests to use Sink/Source pattern
- Add streaming tests (multiple frames, cancellation)
- Add cross-platform tests (C# ↔ Python)

---

## Progress Chart

```
C# Transport Layer:           ████████████████████ 100%  (12/12 - all transports)
C# KeyPoints Sink:            ████████████████████ 100%  (complete)
C# KeyPoints Source:          ░░░░░░░░░░░░░░░░░░░░   0%  (not started)
C# Segmentation Sink:         ░░░░░░░░░░░░░░░░░░░░   0%  (not started)
C# Segmentation Source:       ░░░░░░░░░░░░░░░░░░░░   0%  (not started)
C# Segmentation Writer:       ██████████░░░░░░░░░░  50%  (has bug)
Python Transport Layer:       █████████████░░░░░░░  67%  (4/6)
Python KeyPoints Sink:        ████████████████████ 100%  (complete)
Python KeyPoints Source:      ░░░░░░░░░░░░░░░░░░░░   0%  (not started)
Python Segmentation Writer:   ████████████████████ 100%  (complete)
Python Segmentation Source:   ░░░░░░░░░░░░░░░░░░░░   0%  (not started)
─────────────────────────────────────────────────────────────
OVERALL:                      ████████░░░░░░░░░░░░  ~40%
```

### C# Transport Test Results

```
Total: 55 tests
Passed: 48
Skipped: 7 (4 NNG pub/sub timing, 3 WebSocket integration)
Failed: 0
```

---

## Architecture Reference

See `ARCHITECTURE.md` for:
- Design philosophy (real-time streaming)
- Interface definitions
- Usage examples
- Binary protocol formats

See `REFACTORING_GUIDE.md` for:
- Step-by-step implementation guide
- Code examples
- File checklist

---

**Last Updated:** 2025-12-04
**Status:** ⏳ In Progress - C# Transport Layer complete, protocol implementations ongoing
**Next Step:** Implement `SegmentationResultSource` with `IAsyncEnumerable`
