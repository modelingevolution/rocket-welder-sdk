# Implementation Status: Transport Abstraction Refactor

## Overview

This document tracks the progress of refactoring from `IKeyPointsStorage`/`ISegmentationResultStorage` to the new Sink/Source pattern with transport abstraction.

### Design Goals

1. **Sink** = Writer factory (creates per-frame writers, uses `IFrameSink`)
2. **Source** = Streaming reader (yields frames via `IAsyncEnumerable`, uses `IFrameSource`)
3. **Transport** = Frame boundary handling (length-prefix for streams, native for WebSocket/NNG)

### ⚠️ CRITICAL RULE: ALL Data Uses Framing

**DO NOT REMOVE FRAMING. EVER.**

- ALL protocols MUST use framing (varint for files, 4-byte LE for TCP, native for WS/NNG)
- Python MUST use the same framing as C#
- Files use varint length-prefix framing via `StreamFrameSink`/`StreamFrameSource`
- This is the ENTIRE PURPOSE of the refactor - consistent framing everywhere

### ⚠️ CRITICAL RULE: C# FIRST, THEN PYTHON

**DO NOT TOUCH PYTHON UNTIL C# IS 100% COMPLETE.**

Complete means:
1. ALL C# tests pass (zero failures)
2. Design is correct and follows architecture
3. No unnecessary memory allocations
4. DRY principle followed
5. Code review approved

Only after C# is fully complete and reviewed, work on Python can begin.

---

## Current Status Summary

| Component | Status | Notes |
|-----------|--------|-------|
| **C# Transport Layer** | ✅ 100% | All transports implemented (Stream, TCP, Unix Socket, WebSocket, NNG) |
| **C# KeyPoints Protocol** | ✅ 100% | Sink/Source with IAsyncEnumerable complete |
| **C# Segmentation Protocol** | ✅ 100% | Sink/Source with IAsyncEnumerable complete |
| **C# Tests** | ✅ 100% | 125 passed, 12 skipped, 0 failed |
| **Python Transport Layer** | ⏳ 67% | 4/6 transports working, needs framing update |
| **Python KeyPoints Protocol** | ⏳ 50% | Sink done, Source not implemented |
| **Python Segmentation Protocol** | ⏳ 50% | Writer done, Source not implemented, needs framing |

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

### KeyPoints Protocol ✅

| Component | Status | Notes |
|-----------|--------|-------|
| `IKeyPointsSink` | ✅ | Interface defined |
| `KeyPointsSink` | ✅ | Uses `IFrameSink`, manages delta state |
| `KeyPointsWriter` | ✅ | Buffers to memory, writes atomically |
| `IKeyPointsSource` | ✅ | Interface with `IAsyncEnumerable<KeyPointsFrame>` |
| `KeyPointsSource` | ✅ | Reads via `IFrameSource`, reconstructs delta frames |
| `KeyPointsFrame` | ✅ | Frame struct with frame ID, delta flag, keypoints |
| `KeyPoint` struct | ✅ | Keypoint with ID, X, Y, confidence |

**All KeyPoints tests pass (10/10).**

### Segmentation Protocol ✅

| Component | Status | Notes |
|-----------|--------|-------|
| `ISegmentationResultSink` | ✅ | Interface defined |
| `SegmentationResultSink` | ✅ | Uses `IFrameSink`, creates per-frame writers |
| `SegmentationResultWriter` | ✅ | Buffers to memory, writes atomically via `StreamFrameSink` |
| `ISegmentationResultSource` | ✅ | Interface with `IAsyncEnumerable<SegmentationFrame>` |
| `SegmentationResultSource` | ✅ | Reads via `IFrameSource`, yields frames |
| `SegmentationFrame` | ✅ | Frame struct with instances |
| `SegmentationInstance` | ✅ | Instance struct with points |

**All C# round-trip tests pass.**

### Test Status ✅

**All tests pass: 127 passed, 10 skipped, 0 failed**

Skipped tests:
- 4 NNG Pub/Sub tests (inherent NNG subscription propagation timing limitation)
- 3 WebSocket integration tests (require server infrastructure)
- 3 UiService tests (require EventStore configuration)

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

## C# Code Quality (Completed)

The following code quality improvements were made to the C# implementation:

### Zero-Copy Optimizations

1. **ParseFrame methods**: Use `MemoryMarshal.TryGetArray()` instead of `ToArray()`
   - `KeyPointsSource.ParseFrame()`
   - `SegmentationResultSource.ParseFrame()`

2. **Writer buffer access**: Use `GetBuffer()` instead of `ToArray()`
   - `KeyPointsWriter.Dispose()` and `DisposeAsync()`
   - `SegmentationResultWriter.Flush()` and `FlushAsync()`

### DRY Improvements

1. **KeyPointsWriter**: Extracted `UpdatePreviousFrameState()` method to eliminate duplicated logic in `Dispose()` and `DisposeAsync()`

### Async Best Practices

1. **ConfigureAwait(false)**: Added to all async methods in library code:
   - `KeyPointsSource.ReadFramesAsync()`
   - `KeyPointsWriter.DisposeAsync()`
   - `SegmentationResultSource.ReadFramesAsync()`
   - `SegmentationResultWriter.FlushAsync()`
   - `StreamFrameSink.WriteFrameAsync()`
   - `StreamFrameSource.ReadFrameAsync()`

---

## What Needs To Be Done (Python)

### Priority 1: Python Source Implementations

Same pattern as C# using async generators:

```python
class KeyPointsSource(IKeyPointsSource):
    async def read_frames_async(self) -> AsyncIterator[KeyPointsFrame]:
        while True:
            frame_data = await self._frame_source.read_frame_async()
            if not frame_data:
                return
            yield self._parse_frame(frame_data)
```

### Priority 2: Fix Python Test Dependencies

Add `posix-ipc` to dependencies or make it optional.

### Priority 3: Python Cross-Platform Tests

- Add cross-platform tests (C# ↔ Python)
- Ensure Python uses same framing as C# (varint for files)

---

## Progress Chart

```
C# Transport Layer:           ████████████████████ 100%  (12/12 - all transports)
C# KeyPoints Sink:            ████████████████████ 100%  (complete)
C# KeyPoints Source:          ████████████████████ 100%  (complete with IAsyncEnumerable)
C# Segmentation Sink:         ████████████████████ 100%  (complete)
C# Segmentation Source:       ████████████████████ 100%  (complete with IAsyncEnumerable)
C# Tests:                     ████████████████████ 100%  (125 passed, 12 skipped)
─────────────────────────────────────────────────────────────
C# OVERALL:                   ████████████████████ 100%  COMPLETE
─────────────────────────────────────────────────────────────
Python Transport Layer:       █████████████░░░░░░░  67%  (4/6, needs framing update)
Python KeyPoints Sink:        ████████████████████ 100%  (complete)
Python KeyPoints Source:      ░░░░░░░░░░░░░░░░░░░░   0%  (not started)
Python Segmentation Writer:   ████████████████████ 100%  (complete, needs framing)
Python Segmentation Source:   ░░░░░░░░░░░░░░░░░░░░   0%  (not started)
─────────────────────────────────────────────────────────────
Python OVERALL:               ████████░░░░░░░░░░░░  ~40%  (needs framing + Sources)
```

### C# Test Results

```
Total: 137 tests
Passed: 125
Skipped: 12 (NNG pub/sub, WebSocket integration, UiService, cross-platform Python)
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
**Status:** ✅ C# 100% COMPLETE - Ready for Python implementation
**Next Step:** Implement Python Source classes with async generators
