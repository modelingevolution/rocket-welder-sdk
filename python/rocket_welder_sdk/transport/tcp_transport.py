"""TCP transport with length-prefix framing."""

import contextlib
import socket
import struct
from typing import Optional

from .frame_sink import IFrameSink
from .frame_source import IFrameSource


class TcpFrameSink(IFrameSink):
    """
    Frame sink that writes to a TCP connection with length-prefix framing.

    Each frame is prefixed with a 4-byte little-endian length header.

    Frame format: [Length: 4 bytes LE][Frame Data: N bytes]
    """

    def __init__(self, sock: socket.socket, leave_open: bool = False):
        """
        Create a TCP frame sink.

        Args:
            sock: TCP socket to write to
            leave_open: If True, doesn't close socket on close
        """
        self._socket = sock
        self._leave_open = leave_open
        self._closed = False

    def write_frame(self, frame_data: bytes) -> None:
        """Write frame with length prefix to TCP socket."""
        if self._closed:
            raise ValueError("Cannot write to closed sink")

        # Write 4-byte length prefix (little-endian)
        length_prefix = struct.pack("<I", len(frame_data))
        self._socket.sendall(length_prefix)

        # Write frame data
        self._socket.sendall(frame_data)

    async def write_frame_async(self, frame_data: bytes) -> None:
        """Write frame asynchronously (uses sync socket for now)."""
        self.write_frame(frame_data)

    def flush(self) -> None:
        """Flush is a no-op for TCP (data sent immediately)."""
        pass

    async def flush_async(self) -> None:
        """Flush asynchronously is a no-op for TCP."""
        pass

    def close(self) -> None:
        """Close the TCP sink."""
        if self._closed:
            return
        self._closed = True
        if not self._leave_open:
            with contextlib.suppress(OSError):
                self._socket.shutdown(socket.SHUT_WR)
            self._socket.close()

    async def close_async(self) -> None:
        """Close the TCP sink asynchronously."""
        self.close()


class TcpFrameSource(IFrameSource):
    """
    Frame source that reads from a TCP connection with length-prefix framing.

    Each frame is prefixed with a 4-byte little-endian length header.

    Frame format: [Length: 4 bytes LE][Frame Data: N bytes]
    """

    def __init__(self, sock: socket.socket, leave_open: bool = False):
        """
        Create a TCP frame source.

        Args:
            sock: TCP socket to read from
            leave_open: If True, doesn't close socket on close
        """
        self._socket = sock
        self._leave_open = leave_open
        self._closed = False
        self._end_of_stream = False

    @property
    def has_more_frames(self) -> bool:
        """Check if more frames available."""
        return not self._closed and not self._end_of_stream

    def read_frame(self) -> Optional[bytes]:
        """Read frame with length prefix from TCP socket."""
        if self._closed or self._end_of_stream:
            return None

        # Read 4-byte length prefix
        length_data = self._recv_exactly(4)
        if length_data is None or len(length_data) < 4:
            self._end_of_stream = True
            return None

        frame_length = struct.unpack("<I", length_data)[0]

        if frame_length == 0:
            return b""

        if frame_length > 100 * 1024 * 1024:  # 100 MB sanity check
            raise ValueError(f"Frame length {frame_length} exceeds maximum")

        # Read frame data
        frame_data = self._recv_exactly(frame_length)
        if frame_data is None or len(frame_data) < frame_length:
            self._end_of_stream = True
            raise ValueError(
                f"Incomplete frame data: expected {frame_length}, got {len(frame_data) if frame_data else 0}"
            )

        return frame_data

    async def read_frame_async(self) -> Optional[bytes]:
        """Read frame asynchronously (uses sync socket for now)."""
        return self.read_frame()

    def _recv_exactly(self, n: int) -> Optional[bytes]:
        """Receive exactly n bytes from socket."""
        data = b""
        while len(data) < n:
            chunk = self._socket.recv(n - len(data))
            if not chunk:
                return data if data else None
            data += chunk
        return data

    def close(self) -> None:
        """Close the TCP source."""
        if self._closed:
            return
        self._closed = True
        if not self._leave_open:
            with contextlib.suppress(OSError):
                self._socket.shutdown(socket.SHUT_RD)
            self._socket.close()

    async def close_async(self) -> None:
        """Close the TCP source asynchronously."""
        self.close()
