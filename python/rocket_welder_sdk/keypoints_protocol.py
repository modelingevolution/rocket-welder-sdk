"""KeyPoints protocol - Binary format for efficient keypoint storage.

Binary protocol for efficient streaming of keypoint detection results.
Compatible with C# implementation for cross-platform interoperability.

Protocol:
    Frame Types:
        - Master Frame (0x00): Full keypoint data every N frames
        - Delta Frame (0x01): Delta-encoded changes from previous frame

    Master Frame:
        [FrameType: 1B=0x00][FrameId: 8B LE][KeypointCount: varint]
        [KeypointId: varint][X: 4B LE][Y: 4B LE][Confidence: 2B LE]
        [KeypointId: varint][X: 4B LE][Y: 4B LE][Confidence: 2B LE]
        ...

    Delta Frame:
        [FrameType: 1B=0x01][FrameId: 8B LE][KeypointCount: varint]
        [KeypointId: varint][DeltaX: zigzag varint][DeltaY: zigzag varint][DeltaConf: zigzag varint]
        [KeypointId: varint][DeltaX: zigzag varint][DeltaY: zigzag varint][DeltaConf: zigzag varint]
        ...

JSON Definition:
    {
        "version": "1.0",
        "compute_module_name": "YOLOv8-Pose",
        "points": {
            "nose": 0,
            "left_eye": 1,
            "right_eye": 2,
            ...
        }
    }

Features:
    - Master/delta frame compression for temporal sequences
    - Varint encoding for efficient integer compression
    - ZigZag encoding for signed deltas
    - Confidence stored as ushort (0-10000) internally, float (0.0-1.0) in API
    - Explicit little-endian for cross-platform compatibility
    - Default master frame interval: every 300 frames
"""

import io
import json
import struct
from abc import ABC, abstractmethod
from dataclasses import dataclass
from typing import BinaryIO, Callable, Dict, Iterator, List, Optional, Tuple

import numpy as np
import numpy.typing as npt
from typing_extensions import TypeAlias

from .transport import IFrameSink, StreamFrameSink, StreamFrameSource

# Type aliases
Point = Tuple[int, int]
PointArray: TypeAlias = npt.NDArray[np.int32]  # Shape: (N, 2)

# Frame types
MASTER_FRAME_TYPE = 0x00
DELTA_FRAME_TYPE = 0x01

# Confidence encoding constants
CONFIDENCE_SCALE = 10000.0
CONFIDENCE_MAX = 10000


def _write_varint(stream: BinaryIO, value: int) -> None:
    """Write unsigned integer as varint."""
    if value < 0:
        raise ValueError(f"Varint requires non-negative value, got {value}")

    while value >= 0x80:
        stream.write(bytes([value & 0x7F | 0x80]))
        value >>= 7
    stream.write(bytes([value & 0x7F]))


def _read_varint(stream: BinaryIO) -> int:
    """Read varint from stream and decode to unsigned integer."""
    result = 0
    shift = 0

    while True:
        if shift >= 35:  # Max 5 bytes for uint32
            raise ValueError("Varint too long (corrupted stream)")

        byte_data = stream.read(1)
        if not byte_data:
            raise EOFError("Unexpected end of stream reading varint")

        byte = byte_data[0]
        result |= (byte & 0x7F) << shift
        shift += 7

        if not (byte & 0x80):
            break

    return result


def _zigzag_encode(value: int) -> int:
    """ZigZag encode signed integer to unsigned."""
    return (value << 1) ^ (value >> 31)


def _zigzag_decode(value: int) -> int:
    """ZigZag decode unsigned integer to signed."""
    return (value >> 1) ^ -(value & 1)


def _confidence_to_ushort(confidence: float) -> int:
    """Convert confidence float (0.0-1.0) to ushort (0-10000)."""
    return min(max(int(confidence * CONFIDENCE_SCALE), 0), CONFIDENCE_MAX)


def _confidence_from_ushort(confidence_ushort: int) -> float:
    """Convert confidence ushort (0-10000) to float (0.0-1.0)."""
    return confidence_ushort / CONFIDENCE_SCALE


@dataclass(frozen=True)
class KeyPoint:
    """A single keypoint with position and confidence."""

    keypoint_id: int
    x: int
    y: int
    confidence: float  # 0.0 to 1.0

    def __post_init__(self) -> None:
        """Validate keypoint data."""
        if not 0.0 <= self.confidence <= 1.0:
            raise ValueError(f"Confidence must be in [0.0, 1.0], got {self.confidence}")


@dataclass(frozen=True)
class KeyPointsDefinition:
    """JSON definition mapping keypoint names to IDs."""

    version: str
    compute_module_name: str
    points: Dict[str, int]  # name -> keypoint_id


class IKeyPointsWriter(ABC):
    """Interface for writing keypoints data for a single frame."""

    @abstractmethod
    def append(self, keypoint_id: int, x: int, y: int, confidence: float) -> None:
        """Append a keypoint to this frame."""
        pass

    @abstractmethod
    def append_point(self, keypoint_id: int, point: Point, confidence: float) -> None:
        """Append a keypoint using a Point tuple."""
        pass

    @abstractmethod
    def close(self) -> None:
        """Flush and close the writer."""
        pass

    def __enter__(self) -> "IKeyPointsWriter":
        """Context manager entry."""
        return self

    def __exit__(self, *args: object) -> None:
        """Context manager exit."""
        self.close()


class KeyPointsWriter(IKeyPointsWriter):
    """
    Writes keypoints data for a single frame via IFrameSink.

    Supports master and delta frame encoding for efficient compression.
    Frames are buffered in memory and written atomically on close.

    Thread-safe: No (caller must synchronize)
    """

    def __init__(
        self,
        frame_id: int,
        frame_sink: IFrameSink,
        is_delta: bool,
        previous_frame: Optional[Dict[int, Tuple[Point, int]]] = None,
        on_frame_written: Optional[Callable[[Dict[int, Tuple[Point, int]]], None]] = None,
    ) -> None:
        """
        Initialize writer for a single frame.

        Args:
            frame_id: Unique frame identifier
            frame_sink: IFrameSink to write frame to
            is_delta: True for delta frame, False for master frame
            previous_frame: Previous frame state (required for delta frames)
            on_frame_written: Callback with frame state after writing
        """
        if is_delta and previous_frame is None:
            raise ValueError("Delta frame requires previous_frame")

        self._frame_id = frame_id
        self._frame_sink = frame_sink
        self._buffer = io.BytesIO()  # Buffer frame for atomic write
        self._is_delta = is_delta
        self._previous_frame = previous_frame
        self._on_frame_written = on_frame_written
        self._keypoints: List[Tuple[int, int, int, int]] = []  # (id, x, y, conf_ushort)
        self._disposed = False

    def append(self, keypoint_id: int, x: int, y: int, confidence: float) -> None:
        """
        Append a keypoint to this frame.

        Args:
            keypoint_id: Unique keypoint identifier
            x: X coordinate
            y: Y coordinate
            confidence: Confidence score (0.0 to 1.0)

        Raises:
            ValueError: If confidence is out of range
        """
        if self._disposed:
            raise ValueError("Writer is disposed")

        if not 0.0 <= confidence <= 1.0:
            raise ValueError(f"Confidence must be in [0.0, 1.0], got {confidence}")

        confidence_ushort = _confidence_to_ushort(confidence)
        self._keypoints.append((keypoint_id, x, y, confidence_ushort))

    def append_point(self, keypoint_id: int, point: Point, confidence: float) -> None:
        """Append a keypoint using a Point tuple."""
        self.append(keypoint_id, point[0], point[1], confidence)

    def _write_frame(self) -> None:
        """Write frame to buffer."""
        # Write frame type
        self._buffer.write(bytes([DELTA_FRAME_TYPE if self._is_delta else MASTER_FRAME_TYPE]))

        # Write frame ID (8 bytes, little-endian)
        self._buffer.write(struct.pack("<Q", self._frame_id))

        # Write keypoint count
        _write_varint(self._buffer, len(self._keypoints))

        if self._is_delta and self._previous_frame is not None:
            self._write_delta_keypoints()
        else:
            self._write_master_keypoints()

    def _write_master_keypoints(self) -> None:
        """Write keypoints in master frame format (absolute coordinates)."""
        for keypoint_id, x, y, conf_ushort in self._keypoints:
            # Write keypoint ID
            _write_varint(self._buffer, keypoint_id)

            # Write absolute coordinates (4 bytes each, little-endian)
            self._buffer.write(struct.pack("<i", x))
            self._buffer.write(struct.pack("<i", y))

            # Write confidence (2 bytes, little-endian)
            self._buffer.write(struct.pack("<H", conf_ushort))

    def _write_delta_keypoints(self) -> None:
        """Write keypoints in delta frame format (delta from previous)."""
        assert self._previous_frame is not None

        for keypoint_id, x, y, conf_ushort in self._keypoints:
            # Write keypoint ID
            _write_varint(self._buffer, keypoint_id)

            # Calculate deltas
            if keypoint_id in self._previous_frame:
                prev_point, prev_conf = self._previous_frame[keypoint_id]
                delta_x = x - prev_point[0]
                delta_y = y - prev_point[1]
                delta_conf = conf_ushort - prev_conf
            else:
                # New keypoint - write as absolute
                delta_x = x
                delta_y = y
                delta_conf = conf_ushort

            # Write zigzag-encoded deltas
            _write_varint(self._buffer, _zigzag_encode(delta_x))
            _write_varint(self._buffer, _zigzag_encode(delta_y))
            _write_varint(self._buffer, _zigzag_encode(delta_conf))

    def close(self) -> None:
        """Close writer and flush data via frame sink."""
        if self._disposed:
            return

        self._disposed = True

        # Write frame to buffer
        self._write_frame()

        # Write buffered frame atomically via sink
        frame_data = self._buffer.getvalue()
        self._frame_sink.write_frame(frame_data)

        # Update previous frame state via callback
        if self._on_frame_written is not None:
            frame_state: Dict[int, Tuple[Point, int]] = {}
            for keypoint_id, x, y, conf_ushort in self._keypoints:
                frame_state[keypoint_id] = ((x, y), conf_ushort)
            self._on_frame_written(frame_state)

        # Clean up buffer
        self._buffer.close()


class KeyPointsSeries:
    """
    In-memory representation of keypoints series for efficient querying.

    Provides fast lookup by frame ID and keypoint trajectory queries.
    """

    def __init__(
        self,
        version: str,
        compute_module_name: str,
        points: Dict[str, int],
        index: Dict[int, Dict[int, Tuple[Point, float]]],
    ) -> None:
        """
        Initialize keypoints series.

        Args:
            version: Version of keypoints algorithm/model
            compute_module_name: Name of AI model or assembly
            points: Mapping of keypoint name to ID
            index: Frame ID -> (Keypoint ID -> (Point, confidence))
        """
        self.version = version
        self.compute_module_name = compute_module_name
        self.points = points
        self._index = index

    @property
    def frame_ids(self) -> List[int]:
        """Get all frame IDs in the series."""
        return list(self._index.keys())

    def contains_frame(self, frame_id: int) -> bool:
        """Check if a frame exists in the series."""
        return frame_id in self._index

    def get_frame(self, frame_id: int) -> Optional[Dict[int, Tuple[Point, float]]]:
        """
        Get all keypoints for a specific frame.

        Args:
            frame_id: Frame identifier

        Returns:
            Dictionary mapping keypoint ID to (point, confidence), or None if not found
        """
        return self._index.get(frame_id)

    def get_keypoint(self, frame_id: int, keypoint_id: int) -> Optional[Tuple[Point, float]]:
        """
        Get keypoint position and confidence at specific frame.

        Args:
            frame_id: Frame identifier
            keypoint_id: Keypoint identifier

        Returns:
            (point, confidence) tuple or None if not found
        """
        frame = self._index.get(frame_id)
        if frame is None:
            return None
        return frame.get(keypoint_id)

    def get_keypoint_by_name(
        self, frame_id: int, keypoint_name: str
    ) -> Optional[Tuple[Point, float]]:
        """
        Get keypoint position and confidence at specific frame by name.

        Args:
            frame_id: Frame identifier
            keypoint_name: Keypoint name (e.g., "nose")

        Returns:
            (point, confidence) tuple or None if not found
        """
        keypoint_id = self.points.get(keypoint_name)
        if keypoint_id is None:
            return None
        return self.get_keypoint(frame_id, keypoint_id)

    def get_keypoint_trajectory(self, keypoint_id: int) -> Iterator[Tuple[int, Point, float]]:
        """
        Get trajectory of a specific keypoint across all frames.

        Args:
            keypoint_id: Keypoint identifier

        Yields:
            (frame_id, point, confidence) tuples
        """
        for frame_id, keypoints in self._index.items():
            if keypoint_id in keypoints:
                point, confidence = keypoints[keypoint_id]
                yield (frame_id, point, confidence)

    def get_keypoint_trajectory_by_name(
        self, keypoint_name: str
    ) -> Iterator[Tuple[int, Point, float]]:
        """
        Get trajectory of a specific keypoint by name across all frames.

        Args:
            keypoint_name: Keypoint name (e.g., "nose")

        Yields:
            (frame_id, point, confidence) tuples
        """
        keypoint_id = self.points.get(keypoint_name)
        if keypoint_id is None:
            return

        yield from self.get_keypoint_trajectory(keypoint_id)


class IKeyPointsSink(ABC):
    """Interface for creating keypoints writers and reading keypoints data."""

    @abstractmethod
    def create_writer(self, frame_id: int) -> IKeyPointsWriter:
        """
        Create a writer for the current frame.

        Sink decides whether to write master or delta frame.

        Args:
            frame_id: Unique frame identifier

        Returns:
            KeyPoints writer for this frame
        """
        pass

    @staticmethod
    @abstractmethod
    def read(json_definition: str, blob_stream: BinaryIO) -> KeyPointsSeries:
        """
        Read entire keypoints series into memory for efficient querying.

        Args:
            json_definition: JSON definition string mapping keypoint names to IDs
            blob_stream: Binary stream containing keypoints data

        Returns:
            KeyPointsSeries for in-memory queries
        """
        pass


class KeyPointsSink(IKeyPointsSink):
    """
    Transport-agnostic keypoints sink with master/delta frame compression.

    Manages master frame intervals and provides reading/writing functionality.

    Thread-safe: No (caller must synchronize)
    """

    def __init__(
        self,
        stream: Optional[BinaryIO] = None,
        master_frame_interval: int = 300,
        *,
        frame_sink: Optional[IFrameSink] = None,
        owns_sink: bool = False,
    ) -> None:
        """
        Initialize keypoints sink.

        Args:
            stream: BinaryIO stream (convenience - auto-wraps in StreamFrameSink)
            master_frame_interval: Write master frame every N frames (default: 300)
            frame_sink: IFrameSink to write frames to (keyword-only, transport-agnostic)
            owns_sink: If True, closes the sink on disposal (keyword-only)

        Note:
            Either stream or frame_sink must be provided (not both).
            For convenience, stream is the primary parameter (auto-wraps in StreamFrameSink).
            For transport-agnostic usage, use frame_sink= keyword argument.
        """
        if frame_sink is None and stream is None:
            raise TypeError("Either stream or frame_sink must be provided")

        if frame_sink is not None and stream is not None:
            raise TypeError("Cannot provide both stream and frame_sink")

        if master_frame_interval < 1:
            raise ValueError("master_frame_interval must be >= 1")

        # Convenience: auto-wrap stream in StreamFrameSink
        if stream is not None:
            self._frame_sink: IFrameSink = StreamFrameSink(stream, leave_open=False)
            self._owns_sink = True
        else:
            assert frame_sink is not None
            self._frame_sink = frame_sink
            self._owns_sink = owns_sink

        self._master_frame_interval = master_frame_interval
        self._previous_frame: Optional[Dict[int, Tuple[Point, int]]] = None
        self._frame_count = 0

    def create_writer(self, frame_id: int) -> IKeyPointsWriter:
        """Create a writer for the current frame."""
        is_delta = self._frame_count > 0 and (self._frame_count % self._master_frame_interval) != 0

        def on_frame_written(frame_state: Dict[int, Tuple[Point, int]]) -> None:
            self._previous_frame = frame_state

        writer = KeyPointsWriter(
            frame_id=frame_id,
            frame_sink=self._frame_sink,
            is_delta=is_delta,
            previous_frame=self._previous_frame if is_delta else None,
            on_frame_written=on_frame_written,
        )

        self._frame_count += 1
        return writer

    @staticmethod
    def read(json_definition: str, blob_stream: BinaryIO) -> KeyPointsSeries:
        """Read entire keypoints series into memory."""
        # Parse JSON definition
        definition_dict = json.loads(json_definition)
        version = definition_dict.get("version", "1.0")
        compute_module_name = definition_dict.get("compute_module_name", "")
        points = definition_dict.get("points", {})

        # Use StreamFrameSource to handle varint-prefixed frames
        frame_source = StreamFrameSource(blob_stream, leave_open=True)

        # Read all frames from binary stream
        index: Dict[int, Dict[int, Tuple[Point, float]]] = {}
        current_frame: Dict[int, Tuple[Point, int]] = {}

        while True:
            # Read next frame (handles varint length prefix)
            frame_data = frame_source.read_frame()
            if frame_data is None or len(frame_data) == 0:
                break  # End of stream

            # Parse frame from bytes
            frame_stream = io.BytesIO(frame_data)

            # Read frame type
            frame_type_bytes = frame_stream.read(1)
            if not frame_type_bytes:
                break  # End of stream

            frame_type = frame_type_bytes[0]
            if frame_type == 0xFF:
                break  # End-of-stream marker

            # Read frame ID
            frame_id_bytes = frame_stream.read(8)
            if len(frame_id_bytes) != 8:
                raise EOFError("Failed to read frame ID")
            frame_id = struct.unpack("<Q", frame_id_bytes)[0]

            # Read keypoint count
            keypoint_count = _read_varint(frame_stream)

            frame_keypoints: Dict[int, Tuple[Point, float]] = {}

            if frame_type == MASTER_FRAME_TYPE:
                # Master frame - read absolute coordinates
                current_frame.clear()
                for _ in range(keypoint_count):
                    keypoint_id = _read_varint(frame_stream)

                    # Read absolute coordinates
                    x_bytes = frame_stream.read(4)
                    y_bytes = frame_stream.read(4)
                    if len(x_bytes) != 4 or len(y_bytes) != 4:
                        raise EOFError("Failed to read coordinates")

                    x = struct.unpack("<i", x_bytes)[0]
                    y = struct.unpack("<i", y_bytes)[0]

                    # Read confidence
                    conf_bytes = frame_stream.read(2)
                    if len(conf_bytes) != 2:
                        raise EOFError("Failed to read confidence")
                    conf_ushort = struct.unpack("<H", conf_bytes)[0]

                    point = (x, y)
                    current_frame[keypoint_id] = (point, conf_ushort)
                    frame_keypoints[keypoint_id] = (point, _confidence_from_ushort(conf_ushort))

            elif frame_type == DELTA_FRAME_TYPE:
                # Delta frame - read deltas and reconstruct
                for _ in range(keypoint_count):
                    keypoint_id = _read_varint(frame_stream)

                    delta_x = _zigzag_decode(_read_varint(frame_stream))
                    delta_y = _zigzag_decode(_read_varint(frame_stream))
                    delta_conf = _zigzag_decode(_read_varint(frame_stream))

                    if keypoint_id in current_frame:
                        # Apply delta to previous
                        prev_point, prev_conf = current_frame[keypoint_id]
                        x = prev_point[0] + delta_x
                        y = prev_point[1] + delta_y
                        conf_ushort = max(0, min(CONFIDENCE_MAX, prev_conf + delta_conf))
                    else:
                        # New keypoint - deltas are absolute
                        x = delta_x
                        y = delta_y
                        conf_ushort = max(0, min(CONFIDENCE_MAX, delta_conf))

                    point = (x, y)
                    current_frame[keypoint_id] = (point, conf_ushort)
                    frame_keypoints[keypoint_id] = (point, _confidence_from_ushort(conf_ushort))

            else:
                raise ValueError(f"Unknown frame type: {frame_type}")

            index[frame_id] = frame_keypoints

        return KeyPointsSeries(version, compute_module_name, points, index)
