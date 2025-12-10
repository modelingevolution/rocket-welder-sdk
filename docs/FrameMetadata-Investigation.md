# FrameMetadata Handling Investigation

## Date: 2025-12-10

## Architecture Overview

```
GStreamer Pipeline → zerosink/zerofilter → ZeroBuffer → SDK Controller → User Callback
                     ↓                                    ↓
                     Writes:                              Must Read:
                     [FrameMetadata (16 bytes)]           1. Strip 16-byte prefix
                     [Pixel Data (W×H×C bytes)]           2. Parse FrameMetadata
                                                          3. Create Mat from pixels only
```

## FrameMetadata Structure (16 bytes)

| Offset | Size | Field | Type | Source |
|--------|------|-------|------|--------|
| 0-7 | 8 | frame_number | uint64 | GST_BUFFER_OFFSET (camera) or local counter |
| 8-15 | 8 | timestamp_ns | uint64 | GST_BUFFER_PTS or UINT64_MAX |

**Total: 16 bytes** (was 24 bytes before optimization, comments may be stale)

## Investigation Results Summary

| # | Component | Language | Mode | FrameMetadata Handling | Status |
|---|-----------|----------|------|------------------------|--------|
| 1 | OneWayShmController | Python | OneWay | ❌ **NOT HANDLED** | **BUG** |
| 2 | DuplexShmController | Python | Duplex | ✅ Strips 16 bytes correctly | OK |
| 3 | OneWayShmController | C# | OneWay | ❌ **NOT HANDLED** | **BUG** |
| 4 | DuplexShmController | C# | Duplex | ✅ Strips 16 bytes correctly | OK |

## Known Issue (from integration test)

```
ERROR: Data size mismatch. Expected 230400 bytes for 320x240 with 3 channels, got 230416
```

**Analysis:**
- Expected: 320 × 240 × 3 = 230,400 bytes (just pixels)
- Got: 230,416 bytes = 230,400 + 16 (FrameMetadata prefix)
- **Root Cause**: OneWay controllers don't strip FrameMetadata prefix

---

## Detailed Findings

### 1. Python OneWayShmController - ❌ BUG

**File**: `rocket_welder_sdk/controllers.py`
**Location**: Lines 335-465 (`_create_mat_from_frame()` method)
**Callback signature**: `on_frame: Callable[[Mat], None]` (no FrameMetadata!)

**Problem code (line 365):**
```python
data = np.frombuffer(frame.data, dtype=np.uint8)
```

**Issue**: Reads entire `frame.data` as pixels. Does NOT skip 16-byte FrameMetadata prefix.

**Fix needed**:
1. Read first 16 bytes as FrameMetadata
2. Create Mat from `frame.data[16:]`
3. Consider adding FrameMetadata to callback signature

---

### 2. Python DuplexShmController - ✅ OK

**File**: `rocket_welder_sdk/controllers.py`
**Location**: Lines 703-801 (`_process_duplex_frame()` method)
**Callback signature**: `on_frame: Callable[[FrameMetadata, Mat, Mat], None]`

**Correct code (lines 726-756):**
```python
# Parse FrameMetadata from the beginning of the frame
frame_metadata = FrameMetadata.from_bytes(request_frame.data)

# Calculate pixel data offset and size
pixel_data_offset = FRAME_METADATA_SIZE  # 16
pixel_data_size = request_frame.size - FRAME_METADATA_SIZE

# Create input Mat from pixel data (after metadata prefix)
pixel_data = np.frombuffer(request_frame.data[pixel_data_offset:], dtype=np.uint8)
```

**Status**: Correctly strips 16-byte prefix and passes FrameMetadata to callback.

---

### 3. C# OneWayShmController - ❌ BUG

**File**: `RocketWelder.SDK/OneWayShmController.cs`
**Location**: Lines 100-163 (`ProcessFrames()`) and Lines 165-234 (`ProcessFramesDuplex()`)
**Callback signatures**: `Action<Mat>` and `Action<Mat, Mat>` (no FrameMetadata!)

**Problem code (lines 118, 188, 259, 315):**
```csharp
using var mat = _gstCaps!.Value.CreateMat(frame.Pointer);
```

**Issue**: Passes `frame.Pointer` directly to `CreateMat`, treating entire frame as pixels. Does NOT skip 16-byte FrameMetadata prefix.

**Fix needed**:
1. Read first 16 bytes as FrameMetadata
2. Create Mat from `frame.Pointer + 16`
3. Update `Start(Action<FrameMetadata, Mat, Mat>)` to actually read FrameMetadata (currently synthesizes fake metadata at line 95)

---

### 4. C# DuplexShmController - ✅ OK

**File**: `RocketWelder.SDK/DuplexShmController.cs`
**Location**: Lines 98-141 (`ProcessFrame()` method)
**Callback signature**: `Action<FrameMetadata, Mat, Mat>`

**Correct code (lines 121-130):**
```csharp
// Read FrameMetadata from the beginning of the frame (16 bytes)
var frameMetadata = FrameMetadata.FromPointer((IntPtr)request.Pointer);

// Calculate pointer to actual pixel data (after metadata)
byte* pixelDataPtr = request.Pointer + FrameMetadata.Size;
var pixelDataSize = request.Size - FrameMetadata.Size;

// Create input Mat from pixel data (zero-copy)
using var inputMat = caps.CreateMat(pixelDataPtr);
```

**Status**: Correctly strips 16-byte prefix and passes FrameMetadata to callback.

---

## COMPLETED (All Fixes Applied)

1. [x] ~~Investigate Python OneWayShmController~~ - **BUG FOUND AND FIXED**
2. [x] ~~Investigate Python DuplexShmController~~ - OK
3. [x] ~~Investigate C# OneWayShmController~~ - **BUG FOUND AND FIXED**
4. [x] ~~Investigate C# DuplexShmController~~ - OK
5. [x] **Fixed Python OneWayShmController** - strip 16-byte prefix in `_create_mat_from_frame()`
6. [x] **Fixed C# OneWayShmController** - strip 16-byte prefix, added `ProcessFramesWithMetadata()`
7. [x] **Integration tests pass** - Both Duplex and OneWay modes: 5/5 frames processed

---

## Expected Behavior After Fix

All controllers MUST:
1. Read the first 16 bytes as `FrameMetadata`
2. Parse `frame_number` (bytes 0-7) and `timestamp_ns` (bytes 8-15)
3. Create Mat from bytes starting at offset 16
4. Pass FrameMetadata to callback (or synthesize if callback doesn't accept it for backwards compatibility)

---

## Test Commands

```bash
# Python integration test
cd /mnt/d/source/modelingevolution/rocket-welder-sdk/python
./test_integration.sh

# Manual OneWay test with debug
./venv/bin/python examples/integration_client.py "shm://test_python?mode=OneWay" --exit-after 5 --debug

# Manual Duplex test with debug
./venv/bin/python examples/integration_client.py "shm://test_python?mode=Duplex" --exit-after 5 --debug

# C# tests
cd /mnt/d/source/modelingevolution/rocket-welder-sdk/csharp
dotnet test
```
