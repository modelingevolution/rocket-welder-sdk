"""
Segmentation result serialization protocol.

Binary protocol for efficient streaming of instance segmentation results.
Compatible with C# implementation for cross-platform interoperability.

Protocol (per frame):
    [FrameId: 8B little-endian][Width: varint][Height: varint]
    [classId: 1B][instanceId: 1B][pointCount: varint][points: delta+varint...]
    [classId: 1B][instanceId: 1B][pointCount: varint][points: delta+varint...]
    ...

Features:
    - Delta encoding for adjacent contour points (efficient compression)
    - Varint encoding for variable-length integers
    - ZigZag encoding for signed deltas
    - Explicit little-endian for cross-platform compatibility
    - Frame boundaries handled by transport layer (IFrameSink)
    - NumPy array support for efficient processing
"""

import io
import struct
from abc import ABC, abstractmethod
from dataclasses import dataclass
from typing import (
    AsyncIterator,
    BinaryIO,
    Iterator,
    List,
    Optional,
    Sequence,
    Tuple,
    Union,
)

import numpy as np
import numpy.typing as npt
from typing_extensions import TypeAlias

from .transport import IFrameSink, IFrameSource, StreamFrameSink

# Type aliases
Point = Tuple[int, int]
PointArray: TypeAlias = npt.NDArray[np.int32]  # Shape: (N, 2)


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


@dataclass(frozen=True)
class SegmentationFrameMetadata:
    """Metadata for a segmentation frame."""

    frame_id: int
    width: int
    height: int


@dataclass(frozen=True)
class SegmentationInstance:
    """A single instance in a segmentation result."""

    class_id: int
    instance_id: int
    points: PointArray  # NumPy array of shape (N, 2) with dtype int32

    def to_normalized(self, width: int, height: int) -> npt.NDArray[np.float32]:
        """
        Convert points to normalized coordinates [0-1] range.

        Args:
            width: Frame width in pixels
            height: Frame height in pixels

        Returns:
            NumPy array of shape (N, 2) with dtype float32, normalized to [0-1]
        """
        if width <= 0 or height <= 0:
            raise ValueError("Width and height must be positive")

        # Vectorized operation - very efficient
        normalized = self.points.astype(np.float32)
        normalized[:, 0] /= width
        normalized[:, 1] /= height
        return normalized

    def to_list(self) -> List[Point]:
        """Convert points to list of tuples."""
        return [(int(x), int(y)) for x, y in self.points]


@dataclass(frozen=True)
class SegmentationFrame:
    """
    Represents a decoded segmentation frame containing instance segmentation results.

    Matches C# SegmentationFrame record struct.
    Used for round-trip testing of segmentation protocol encoding/decoding.

    Attributes:
        frame_id: Frame identifier for temporal ordering.
        width: Frame width in pixels.
        height: Frame height in pixels.
        instances: Segmentation instances detected in this frame.
    """

    frame_id: int
    width: int
    height: int
    instances: Sequence[SegmentationInstance]

    @property
    def count(self) -> int:
        """Number of instances in the frame."""
        return len(self.instances)

    def find_by_class(self, class_id: int) -> List[SegmentationInstance]:
        """Find all instances with the specified class ID."""
        return [inst for inst in self.instances if inst.class_id == class_id]

    def find_by_instance(self, instance_id: int) -> List[SegmentationInstance]:
        """Find all instances with the specified instance ID."""
        return [inst for inst in self.instances if inst.instance_id == instance_id]


class SegmentationResultWriter:
    """
    Writes segmentation results for a single frame via IFrameSink.

    Frames are buffered in memory and written atomically on close.

    Thread-safe: No (caller must synchronize)
    """

    def __init__(
        self,
        frame_id: int,
        width: int,
        height: int,
        stream: Optional[BinaryIO] = None,
        *,
        frame_sink: Optional[IFrameSink] = None,
    ) -> None:
        """
        Initialize writer for a single frame.

        Args:
            frame_id: Unique frame identifier
            width: Frame width in pixels
            height: Frame height in pixels
            stream: Binary stream (convenience - auto-wraps in StreamFrameSink)
            frame_sink: IFrameSink to write frame to (keyword-only, transport-agnostic)

        Note:
            Either stream or frame_sink must be provided (not both).
            For convenience, stream is the primary parameter (auto-wraps in StreamFrameSink).
        """
        if frame_sink is None and stream is None:
            raise TypeError("Either stream or frame_sink must be provided")

        if frame_sink is not None and stream is not None:
            raise TypeError("Cannot provide both stream and frame_sink")

        # Convenience: auto-wrap stream in StreamFrameSink
        if stream is not None:
            self._frame_sink: IFrameSink = StreamFrameSink(stream, leave_open=True)
            self._owns_sink = False  # Don't close the stream wrapper
        else:
            assert frame_sink is not None
            self._frame_sink = frame_sink
            self._owns_sink = False

        self._frame_id = frame_id
        self._width = width
        self._height = height
        self._buffer = io.BytesIO()  # Buffer frame for atomic write
        self._header_written = False
        self._disposed = False

    def _ensure_header_written(self) -> None:
        """Write frame header to buffer if not already written."""
        if self._header_written:
            return

        # Write FrameId (8 bytes, little-endian)
        self._buffer.write(struct.pack("<Q", self._frame_id))

        # Write Width and Height as varints
        _write_varint(self._buffer, self._width)
        _write_varint(self._buffer, self._height)

        self._header_written = True

    def append(
        self,
        class_id: int,
        instance_id: int,
        points: Union[List[Point], PointArray],
    ) -> None:
        """
        Append an instance with contour points.

        Args:
            class_id: Object class ID (0-255)
            instance_id: Instance ID within class (0-255)
            points: List of (x, y) tuples or NumPy array of shape (N, 2)
        """
        if class_id < 0 or class_id > 255:
            raise ValueError(f"class_id must be 0-255, got {class_id}")
        if instance_id < 0 or instance_id > 255:
            raise ValueError(f"instance_id must be 0-255, got {instance_id}")

        self._ensure_header_written()

        # Convert to NumPy array if needed
        if not isinstance(points, np.ndarray):
            points_array = np.array(points, dtype=np.int32)
        else:
            points_array = points.astype(np.int32)

        if points_array.ndim != 2 or points_array.shape[1] != 2:
            raise ValueError(f"Points must be shape (N, 2), got {points_array.shape}")

        # Write class_id and instance_id
        self._buffer.write(bytes([class_id, instance_id]))

        # Write point count
        point_count = len(points_array)
        _write_varint(self._buffer, point_count)

        if point_count == 0:
            return

        # Write first point (absolute coordinates)
        first_point = points_array[0]
        _write_varint(self._buffer, _zigzag_encode(int(first_point[0])))
        _write_varint(self._buffer, _zigzag_encode(int(first_point[1])))

        # Write remaining points (delta encoded)
        for i in range(1, point_count):
            delta_x = int(points_array[i, 0] - points_array[i - 1, 0])
            delta_y = int(points_array[i, 1] - points_array[i - 1, 1])
            _write_varint(self._buffer, _zigzag_encode(delta_x))
            _write_varint(self._buffer, _zigzag_encode(delta_y))

    def flush(self) -> None:
        """Flush buffered frame via frame sink without closing."""
        if self._disposed:
            return

        # Ensure header is written (even if no instances appended)
        self._ensure_header_written()

        # Write buffered frame atomically via sink
        frame_data = self._buffer.getvalue()
        self._frame_sink.write_frame(frame_data)
        self._frame_sink.flush()

    def close(self) -> None:
        """Close writer and write buffered frame via frame sink."""
        if self._disposed:
            return

        self._disposed = True

        # Ensure header is written (even if no instances appended)
        self._ensure_header_written()

        # Send complete frame atomically via sink
        frame_data = self._buffer.getvalue()
        self._frame_sink.write_frame(frame_data)

        # Clean up buffer
        self._buffer.close()

    def __enter__(self) -> "SegmentationResultWriter":
        """Context manager entry."""
        return self

    def __exit__(self, *args: object) -> None:
        """Context manager exit."""
        self.close()


class SegmentationResultReader:
    """
    Reads segmentation results for a single frame.

    Thread-safe: No (caller must synchronize)
    Stream ownership: Caller must close stream
    """

    def __init__(self, stream: BinaryIO) -> None:
        """
        Initialize reader for a single frame.

        Args:
            stream: Binary stream to read from (must support read()).
                    Should contain raw frame data without length prefix.
                    Use StreamFrameSource to strip length prefixes from transport streams.
        """
        if not hasattr(stream, "read"):
            raise TypeError("Stream must be a binary readable stream")

        self._stream = stream
        self._header_read = False
        self._metadata: Optional[SegmentationFrameMetadata] = None

        # Max points per instance - prevents OOM attacks
        self._max_points_per_instance = 10_000_000  # 10M points

    def _ensure_header_read(self) -> None:
        """Read frame header if not already read."""
        if self._header_read:
            return

        # Read FrameId (8 bytes, little-endian)
        frame_id_bytes = self._stream.read(8)
        if len(frame_id_bytes) != 8:
            raise EOFError("Failed to read FrameId")
        frame_id = struct.unpack("<Q", frame_id_bytes)[0]

        # Read Width and Height as varints
        width = _read_varint(self._stream)
        height = _read_varint(self._stream)

        self._metadata = SegmentationFrameMetadata(frame_id, width, height)
        self._header_read = True

    @property
    def metadata(self) -> SegmentationFrameMetadata:
        """Get frame metadata (frameId, width, height)."""
        self._ensure_header_read()
        assert self._metadata is not None
        return self._metadata

    def read_next(self) -> Optional[SegmentationInstance]:
        """
        Read next instance from stream.

        Returns:
            SegmentationInstance if available, None if end of stream reached

        Raises:
            EOFError: If stream ends unexpectedly
            ValueError: If data is corrupted
        """
        self._ensure_header_read()

        # Read class_id and instance_id (buffered for performance)
        header = self._stream.read(2)

        if len(header) == 0:
            # End of stream - no more instances
            return None

        if len(header) != 2:
            raise EOFError("Unexpected end of stream reading instance header")

        class_id = header[0]
        instance_id = header[1]

        # Read point count with validation
        point_count = _read_varint(self._stream)
        if point_count > self._max_points_per_instance:
            raise ValueError(
                f"Point count {point_count} exceeds maximum " f"{self._max_points_per_instance}"
            )

        if point_count == 0:
            # Empty points array
            points = np.empty((0, 2), dtype=np.int32)
            return SegmentationInstance(class_id, instance_id, points)

        # Allocate NumPy array for points
        points = np.empty((point_count, 2), dtype=np.int32)

        # Read first point (absolute coordinates)
        x = _zigzag_decode(_read_varint(self._stream))
        y = _zigzag_decode(_read_varint(self._stream))
        points[0] = [x, y]

        # Read remaining points (delta encoded)
        for i in range(1, point_count):
            delta_x = _zigzag_decode(_read_varint(self._stream))
            delta_y = _zigzag_decode(_read_varint(self._stream))
            x += delta_x
            y += delta_y
            points[i] = [x, y]

        return SegmentationInstance(class_id, instance_id, points)

    def read_all(self) -> List[SegmentationInstance]:
        """
        Read all instances from frame.

        Returns:
            List of all instances in frame
        """
        instances = []
        while True:
            instance = self.read_next()
            if instance is None:
                break
            instances.append(instance)
        return instances

    def __iter__(self) -> Iterator[SegmentationInstance]:
        """Iterate over instances in frame."""
        while True:
            instance = self.read_next()
            if instance is None:
                break
            yield instance

    def __enter__(self) -> "SegmentationResultReader":
        """Context manager entry."""
        return self

    def __exit__(self, *args: object) -> None:
        """Context manager exit."""
        pass


class ISegmentationResultWriter(ABC):
    """Interface for writing segmentation results for a single frame."""

    @abstractmethod
    def append(
        self,
        class_id: int,
        instance_id: int,
        points: Union[List[Point], PointArray],
    ) -> None:
        """
        Append an instance with contour points.

        Args:
            class_id: Object class ID (0-255)
            instance_id: Instance ID within class (0-255)
            points: List of (x, y) tuples or NumPy array of shape (N, 2)
        """
        pass

    @abstractmethod
    def close(self) -> None:
        """Flush and close the writer."""
        pass

    def __enter__(self) -> "ISegmentationResultWriter":
        """Context manager entry."""
        return self

    def __exit__(self, *args: object) -> None:
        """Context manager exit."""
        self.close()


class ISegmentationResultSink(ABC):
    """
    Factory for creating segmentation result writers per frame (transport-agnostic).

    Mirrors C# ISegmentationResultSink interface.
    """

    @abstractmethod
    def create_writer(self, frame_id: int, width: int, height: int) -> ISegmentationResultWriter:
        """
        Create a writer for the current frame.

        Args:
            frame_id: Unique frame identifier
            width: Frame width in pixels
            height: Frame height in pixels

        Returns:
            Segmentation result writer for this frame
        """
        pass

    @abstractmethod
    def close(self) -> None:
        """Close the sink and release resources."""
        pass

    def __enter__(self) -> "ISegmentationResultSink":
        """Context manager entry."""
        return self

    def __exit__(self, *args: object) -> None:
        """Context manager exit."""
        self.close()


class SegmentationResultSink(ISegmentationResultSink):
    """
    Transport-agnostic segmentation result sink.

    Creates writers for each frame that serialize to the underlying IFrameSink.

    Thread-safe: No (caller must synchronize)
    """

    def __init__(
        self,
        stream: Optional[BinaryIO] = None,
        *,
        frame_sink: Optional[IFrameSink] = None,
        owns_sink: bool = False,
    ) -> None:
        """
        Initialize segmentation result sink.

        Args:
            stream: BinaryIO stream (convenience - auto-wraps in StreamFrameSink)
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

        # Convenience: auto-wrap stream in StreamFrameSink
        if stream is not None:
            self._frame_sink: IFrameSink = StreamFrameSink(stream, leave_open=False)
            self._owns_sink = True
        else:
            assert frame_sink is not None
            self._frame_sink = frame_sink
            self._owns_sink = owns_sink

    def create_writer(self, frame_id: int, width: int, height: int) -> ISegmentationResultWriter:
        """Create a writer for the current frame."""
        # SegmentationResultWriter implements the write methods we need
        # We return it as ISegmentationResultWriter
        return SegmentationResultWriter(  # type: ignore[return-value]
            frame_id=frame_id,
            width=width,
            height=height,
            frame_sink=self._frame_sink,
        )

    def close(self) -> None:
        """Close the sink and release resources."""
        if self._owns_sink:
            self._frame_sink.close()


class ISegmentationResultSource(ABC):
    """
    Interface for streaming segmentation frames from a source.

    Mirrors the pattern from IKeyPointsSource for consistency.
    """

    @abstractmethod
    def read_frames(self) -> Iterator[SegmentationFrame]:
        """
        Read frames synchronously as an iterator.

        Yields:
            SegmentationFrame for each frame in the source.
        """
        pass

    def read_frames_async(self) -> AsyncIterator[SegmentationFrame]:
        """
        Read frames asynchronously as an async iterator.

        Yields:
            SegmentationFrame for each frame in the source.

        Raises:
            NotImplementedError: Subclass must implement for async support.
        """
        raise NotImplementedError("Subclass must implement read_frames_async")

    def close(self) -> None:  # noqa: B027
        """Close the source and release resources."""
        pass

    async def close_async(self) -> None:  # noqa: B027
        """Close the source and release resources asynchronously."""
        pass

    def __enter__(self) -> "ISegmentationResultSource":
        """Context manager entry."""
        return self

    def __exit__(self, *args: object) -> None:
        """Context manager exit."""
        self.close()

    async def __aenter__(self) -> "ISegmentationResultSource":
        """Async context manager entry."""
        return self

    async def __aexit__(self, *args: object) -> None:
        """Async context manager exit."""
        await self.close_async()


class SegmentationResultSource(ISegmentationResultSource):
    """
    High-level segmentation result source that reads from any IFrameSource.

    Wraps IFrameSource to provide iterator-based access to SegmentationFrame objects.

    Thread-safe: No (caller must synchronize)
    """

    def __init__(self, frame_source: IFrameSource) -> None:
        """
        Create a segmentation result source.

        Args:
            frame_source: Low-level frame source to read from.
        """
        self._frame_source = frame_source
        self._closed = False

    def read_frames(self) -> Iterator[SegmentationFrame]:
        """
        Read frames synchronously as an iterator.

        Yields:
            SegmentationFrame for each frame in the source.

        Raises:
            RuntimeError: If source is closed.
        """
        if self._closed:
            raise RuntimeError("SegmentationResultSource is closed")

        while True:
            frame_data = self._frame_source.read_frame()
            if frame_data is None or len(frame_data) == 0:
                break

            frame = SegmentationProtocol.read(frame_data)
            yield frame

    async def read_frames_async(self) -> AsyncIterator[SegmentationFrame]:
        """
        Read frames asynchronously as an async iterator.

        Yields:
            SegmentationFrame for each frame in the source.

        Raises:
            RuntimeError: If source is closed.
        """
        if self._closed:
            raise RuntimeError("SegmentationResultSource is closed")

        while True:
            frame_data = await self._frame_source.read_frame_async()
            if frame_data is None or len(frame_data) == 0:
                break

            frame = SegmentationProtocol.read(frame_data)
            yield frame

    def close(self) -> None:
        """Close the source and release resources."""
        if self._closed:
            return
        self._closed = True
        self._frame_source.close()

    async def close_async(self) -> None:
        """Close the source and release resources asynchronously."""
        if self._closed:
            return
        self._closed = True
        await self._frame_source.close_async()


class SegmentationProtocol:
    """
    Static helpers for encoding and decoding segmentation protocol data.

    Pure protocol logic with no transport or rendering dependencies.
    Matches C# SegmentationProtocol static class.

    Frame Format:
        [FrameId: 8 bytes, little-endian uint64]
        [Width: varint]
        [Height: varint]
        [Instances...]

    Instance Format:
        [ClassId: 1 byte]
        [InstanceId: 1 byte]
        [PointCount: varint]
        [Point0: X zigzag-varint, Y zigzag-varint]  (absolute)
        [Point1+: deltaX zigzag-varint, deltaY zigzag-varint]
    """

    @staticmethod
    def write(buffer: bytearray, frame: SegmentationFrame) -> int:
        """
        Write a complete segmentation frame to a buffer.

        Args:
            buffer: Pre-allocated buffer to write to.
            frame: Frame to encode.

        Returns:
            Number of bytes written.
        """
        stream = io.BytesIO()

        # Write header
        stream.write(struct.pack("<Q", frame.frame_id))
        _write_varint(stream, frame.width)
        _write_varint(stream, frame.height)

        # Write instances
        for instance in frame.instances:
            SegmentationProtocol._write_instance_core(stream, instance)

        data = stream.getvalue()
        buffer[: len(data)] = data
        return len(data)

    @staticmethod
    def _write_instance_core(stream: BinaryIO, instance: SegmentationInstance) -> None:
        """Write a single instance to the stream."""
        stream.write(bytes([instance.class_id, instance.instance_id]))
        point_count = len(instance.points)
        _write_varint(stream, point_count)

        if point_count == 0:
            return

        prev_x, prev_y = 0, 0
        for i, point in enumerate(instance.points):
            x, y = int(point[0]), int(point[1])
            if i == 0:
                # First point is absolute (but still zigzag encoded)
                _write_varint(stream, _zigzag_encode(x))
                _write_varint(stream, _zigzag_encode(y))
            else:
                # Subsequent points are deltas
                _write_varint(stream, _zigzag_encode(x - prev_x))
                _write_varint(stream, _zigzag_encode(y - prev_y))
            prev_x, prev_y = x, y

    @staticmethod
    def write_header(buffer: bytearray, frame_id: int, width: int, height: int) -> int:
        """
        Write just the frame header (frameId, width, height).

        Args:
            buffer: Pre-allocated buffer to write to.
            frame_id: Frame identifier.
            width: Frame width.
            height: Frame height.

        Returns:
            Number of bytes written.
        """
        stream = io.BytesIO()
        stream.write(struct.pack("<Q", frame_id))
        _write_varint(stream, width)
        _write_varint(stream, height)
        data = stream.getvalue()
        buffer[: len(data)] = data
        return len(data)

    @staticmethod
    def write_instance(
        buffer: bytearray,
        class_id: int,
        instance_id: int,
        points: Union[List[Point], PointArray],
    ) -> int:
        """
        Write a single segmentation instance.

        Points are delta-encoded for compression.

        Args:
            buffer: Pre-allocated buffer to write to.
            class_id: Class identifier (0-255).
            instance_id: Instance identifier (0-255).
            points: Polygon points.

        Returns:
            Number of bytes written.
        """
        # Convert to numpy array if needed
        if not isinstance(points, np.ndarray):
            points_array = np.array(points, dtype=np.int32)
        else:
            points_array = points.astype(np.int32)

        instance = SegmentationInstance(class_id, instance_id, points_array)

        stream = io.BytesIO()
        SegmentationProtocol._write_instance_core(stream, instance)
        data = stream.getvalue()
        buffer[: len(data)] = data
        return len(data)

    @staticmethod
    def calculate_instance_size(point_count: int) -> int:
        """
        Calculate the maximum buffer size needed for an instance.

        Args:
            point_count: Number of polygon points.

        Returns:
            Maximum bytes needed.
        """
        # classId(1) + instanceId(1) + pointCount(varint, max 5) + points(max 10 bytes each)
        return 1 + 1 + 5 + (point_count * 10)

    @staticmethod
    def read(data: bytes) -> SegmentationFrame:
        """
        Read a complete segmentation frame from a buffer.

        Args:
            data: Raw frame data.

        Returns:
            Decoded SegmentationFrame.
        """
        stream = io.BytesIO(data)

        # Read header
        frame_id_bytes = stream.read(8)
        if len(frame_id_bytes) != 8:
            raise EOFError("Failed to read FrameId")
        frame_id = struct.unpack("<Q", frame_id_bytes)[0]
        width = _read_varint(stream)
        height = _read_varint(stream)

        # Read all instances
        instances: List[SegmentationInstance] = []
        while True:
            header = stream.read(2)
            if len(header) == 0:
                break
            if len(header) != 2:
                raise EOFError("Unexpected end of stream reading instance header")

            class_id = header[0]
            instance_id = header[1]
            point_count = _read_varint(stream)

            if point_count == 0:
                points = np.empty((0, 2), dtype=np.int32)
            else:
                points = np.empty((point_count, 2), dtype=np.int32)
                prev_x, prev_y = 0, 0

                for i in range(point_count):
                    x = _zigzag_decode(_read_varint(stream))
                    y = _zigzag_decode(_read_varint(stream))
                    if i > 0:
                        x += prev_x
                        y += prev_y
                    points[i] = [x, y]
                    prev_x, prev_y = x, y

            instances.append(SegmentationInstance(class_id, instance_id, points))

        return SegmentationFrame(frame_id, width, height, instances)

    @staticmethod
    def try_read(data: bytes) -> Optional[SegmentationFrame]:
        """
        Try to read a segmentation frame, returning None if the data is invalid.

        Args:
            data: Raw frame data.

        Returns:
            SegmentationFrame if successful, None otherwise.
        """
        try:
            return SegmentationProtocol.read(data)
        except Exception:
            return None
