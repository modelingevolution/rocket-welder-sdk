"""Stream-based transport (file, memory, etc.)."""

from typing import BinaryIO, Optional

from .frame_sink import IFrameSink
from .frame_source import IFrameSource


def _write_varint(stream: BinaryIO, value: int) -> None:
    """Write unsigned integer as varint (Protocol Buffers format)."""
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


class StreamFrameSink(IFrameSink):
    """
    Frame sink that writes to a BinaryIO stream (file, memory, etc.).

    Each frame is prefixed with its length (varint encoding) for frame boundary detection.
    Format: [varint length][frame data]
    """

    def __init__(self, stream: BinaryIO, leave_open: bool = False):
        """
        Create a stream-based frame sink.

        Args:
            stream: Binary stream to write to
            leave_open: If True, doesn't close stream on close
        """
        self._stream = stream
        self._leave_open = leave_open
        self._closed = False

    def write_frame(self, frame_data: bytes) -> None:
        """Write frame data to stream with varint length prefix."""
        if self._closed:
            raise ValueError("Cannot write to closed sink")

        # Write frame length as varint
        _write_varint(self._stream, len(frame_data))

        # Write frame data
        self._stream.write(frame_data)

    async def write_frame_async(self, frame_data: bytes) -> None:
        """Write frame data to stream asynchronously."""
        # For regular streams, just use synchronous write
        # If stream supports async, could use aiofiles
        self.write_frame(frame_data)

    def flush(self) -> None:
        """Flush buffered data to stream."""
        if not self._closed:
            self._stream.flush()

    async def flush_async(self) -> None:
        """Flush buffered data to stream asynchronously."""
        self.flush()

    def close(self) -> None:
        """Close the sink."""
        if self._closed:
            return
        self._closed = True
        if not self._leave_open:
            self._stream.close()

    async def close_async(self) -> None:
        """Close the sink asynchronously."""
        self.close()


class StreamFrameSource(IFrameSource):
    """
    Frame source that reads from a BinaryIO stream (file, memory, etc.).

    Reads frames prefixed with varint length for frame boundary detection.
    Format: [varint length][frame data]
    """

    def __init__(self, stream: BinaryIO, leave_open: bool = False):
        """
        Create a stream-based frame source.

        Args:
            stream: Binary stream to read from
            leave_open: If True, doesn't close stream on close
        """
        self._stream = stream
        self._leave_open = leave_open
        self._closed = False

    @property
    def has_more_frames(self) -> bool:
        """Check if more data available in stream."""
        if self._closed:
            return False
        current_pos = self._stream.tell()
        # Try seeking to end to check size
        try:
            self._stream.seek(0, 2)  # Seek to end
            end_pos = self._stream.tell()
            self._stream.seek(current_pos)  # Restore position
            return current_pos < end_pos
        except OSError:
            # Stream not seekable, assume data available
            return True

    def read_frame(self) -> Optional[bytes]:
        """
        Read frame from stream with varint length-prefix framing.

        Returns:
            Frame data bytes, or None if end of stream
        """
        if self._closed:
            return None

        # Check if stream has data (for seekable streams)
        if hasattr(self._stream, "tell") and hasattr(self._stream, "seek"):
            try:
                current_pos = self._stream.tell()
                self._stream.seek(0, 2)  # Seek to end
                end_pos = self._stream.tell()
                self._stream.seek(current_pos)  # Restore position
                if current_pos >= end_pos:
                    return None
            except OSError:
                pass  # Stream not seekable, continue

        # Read frame length (varint)
        try:
            frame_length = _read_varint(self._stream)
        except EOFError:
            return None

        if frame_length == 0:
            return b""

        # Read frame data
        frame_data = self._stream.read(frame_length)
        if len(frame_data) != frame_length:
            raise EOFError(
                f"Unexpected end of stream while reading frame. Expected {frame_length} bytes, got {len(frame_data)}"
            )

        return frame_data

    async def read_frame_async(self) -> Optional[bytes]:
        """Read frame from stream asynchronously."""
        # For regular streams, just use synchronous read
        return self.read_frame()

    def close(self) -> None:
        """Close the source."""
        if self._closed:
            return
        self._closed = True
        if not self._leave_open:
            self._stream.close()

    async def close_async(self) -> None:
        """Close the source asynchronously."""
        self.close()
