"""Unix Domain Socket transport with length-prefix framing.

Frame format: [Length: 4 bytes LE][Frame Data: N bytes]
Unix Domain Sockets provide high-performance IPC on Linux/macOS.
"""

import asyncio
import contextlib
import os
import socket
import struct
from typing import Optional

from .frame_sink import IFrameSink
from .frame_source import IFrameSource


class UnixSocketFrameSink(IFrameSink):
    """
    Frame sink that writes to a Unix Domain Socket with length-prefix framing.

    Each frame is prefixed with a 4-byte little-endian length header.
    """

    def __init__(self, sock: socket.socket, leave_open: bool = False):
        """
        Create a Unix socket frame sink.

        Args:
            sock: Connected Unix domain socket
            leave_open: If True, doesn't close socket on close
        """
        if sock.family != socket.AF_UNIX:
            raise ValueError("Socket must be a Unix domain socket")

        self._socket = sock
        self._leave_open = leave_open
        self._closed = False

    @classmethod
    def connect(cls, socket_path: str) -> "UnixSocketFrameSink":
        """
        Connect to a Unix socket path and create a frame sink.

        Args:
            socket_path: Path to Unix socket file

        Returns:
            Connected frame sink
        """
        sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        sock.connect(socket_path)
        return cls(sock, leave_open=False)

    @classmethod
    async def connect_async(cls, socket_path: str) -> "UnixSocketFrameSink":
        """
        Connect to a Unix socket path asynchronously and create a frame sink.

        Args:
            socket_path: Path to Unix socket file

        Returns:
            Connected frame sink
        """
        sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        sock.setblocking(False)
        loop = asyncio.get_event_loop()
        await loop.sock_connect(sock, socket_path)
        return cls(sock, leave_open=False)

    def write_frame(self, frame_data: bytes) -> None:
        """Write frame with 4-byte length prefix to Unix socket."""
        if self._closed:
            raise ValueError("Cannot write to closed sink")

        # Write 4-byte length prefix (little-endian)
        length_prefix = struct.pack("<I", len(frame_data))
        self._socket.sendall(length_prefix)

        # Write frame data
        self._socket.sendall(frame_data)

    async def write_frame_async(self, frame_data: bytes) -> None:
        """Write frame asynchronously."""
        if self._closed:
            raise ValueError("Cannot write to closed sink")

        loop = asyncio.get_event_loop()

        # Write 4-byte length prefix (little-endian)
        length_prefix = struct.pack("<I", len(frame_data))
        await loop.sock_sendall(self._socket, length_prefix)

        # Write frame data
        await loop.sock_sendall(self._socket, frame_data)

    def flush(self) -> None:
        """Flush is a no-op for Unix sockets (data sent immediately)."""
        pass

    async def flush_async(self) -> None:
        """Flush asynchronously is a no-op for Unix sockets."""
        pass

    def close(self) -> None:
        """Close the Unix socket sink."""
        if self._closed:
            return
        self._closed = True
        if not self._leave_open:
            with contextlib.suppress(OSError):
                self._socket.shutdown(socket.SHUT_WR)
            self._socket.close()

    async def close_async(self) -> None:
        """Close the Unix socket sink asynchronously."""
        self.close()


class UnixSocketFrameSource(IFrameSource):
    """
    Frame source that reads from a Unix Domain Socket with length-prefix framing.

    Each frame is prefixed with a 4-byte little-endian length header.
    """

    # Maximum frame size (100 MB)
    MAX_FRAME_SIZE = 100 * 1024 * 1024

    def __init__(self, sock: socket.socket, leave_open: bool = False):
        """
        Create a Unix socket frame source.

        Args:
            sock: Connected Unix domain socket
            leave_open: If True, doesn't close socket on close
        """
        if sock.family != socket.AF_UNIX:
            raise ValueError("Socket must be a Unix domain socket")

        self._socket = sock
        self._leave_open = leave_open
        self._closed = False
        self._end_of_stream = False

    @classmethod
    def connect(cls, socket_path: str) -> "UnixSocketFrameSource":
        """
        Connect to a Unix socket path and create a frame source.

        Args:
            socket_path: Path to Unix socket file

        Returns:
            Connected frame source
        """
        sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        sock.connect(socket_path)
        return cls(sock, leave_open=False)

    @classmethod
    async def connect_async(cls, socket_path: str) -> "UnixSocketFrameSource":
        """
        Connect to a Unix socket path asynchronously and create a frame source.

        Args:
            socket_path: Path to Unix socket file

        Returns:
            Connected frame source
        """
        sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        sock.setblocking(False)
        loop = asyncio.get_event_loop()
        await loop.sock_connect(sock, socket_path)
        return cls(sock, leave_open=False)

    @property
    def has_more_frames(self) -> bool:
        """Check if more frames available."""
        return not self._closed and not self._end_of_stream

    def _recv_exactly(self, n: int) -> Optional[bytes]:
        """Receive exactly n bytes from socket."""
        data = b""
        while len(data) < n:
            chunk = self._socket.recv(n - len(data))
            if not chunk:
                return data if data else None
            data += chunk
        return data

    async def _recv_exactly_async(self, n: int) -> Optional[bytes]:
        """Receive exactly n bytes from socket asynchronously."""
        loop = asyncio.get_event_loop()
        data = b""
        while len(data) < n:
            chunk = await loop.sock_recv(self._socket, n - len(data))
            if not chunk:
                return data if data else None
            data += chunk
        return data

    def read_frame(self) -> Optional[bytes]:
        """Read frame with 4-byte length prefix from Unix socket."""
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

        if frame_length > self.MAX_FRAME_SIZE:
            raise ValueError(f"Frame length {frame_length} exceeds maximum {self.MAX_FRAME_SIZE}")

        # Read frame data
        frame_data = self._recv_exactly(frame_length)
        if frame_data is None or len(frame_data) < frame_length:
            self._end_of_stream = True
            raise ValueError(
                f"Incomplete frame data: expected {frame_length}, "
                f"got {len(frame_data) if frame_data else 0}"
            )

        return frame_data

    async def read_frame_async(self) -> Optional[bytes]:
        """Read frame asynchronously."""
        if self._closed or self._end_of_stream:
            return None

        # Read 4-byte length prefix
        length_data = await self._recv_exactly_async(4)
        if length_data is None or len(length_data) < 4:
            self._end_of_stream = True
            return None

        frame_length = struct.unpack("<I", length_data)[0]

        if frame_length == 0:
            return b""

        if frame_length > self.MAX_FRAME_SIZE:
            raise ValueError(f"Frame length {frame_length} exceeds maximum {self.MAX_FRAME_SIZE}")

        # Read frame data
        frame_data = await self._recv_exactly_async(frame_length)
        if frame_data is None or len(frame_data) < frame_length:
            self._end_of_stream = True
            raise ValueError(
                f"Incomplete frame data: expected {frame_length}, "
                f"got {len(frame_data) if frame_data else 0}"
            )

        return frame_data

    def close(self) -> None:
        """Close the Unix socket source."""
        if self._closed:
            return
        self._closed = True
        if not self._leave_open:
            with contextlib.suppress(OSError):
                self._socket.shutdown(socket.SHUT_RD)
            self._socket.close()

    async def close_async(self) -> None:
        """Close the Unix socket source asynchronously."""
        self.close()


class UnixSocketServer:
    """
    Helper class to create a Unix socket server that accepts connections.
    """

    def __init__(self, socket_path: str):
        """
        Create a Unix socket server.

        Args:
            socket_path: Path to Unix socket file
        """
        self._socket_path = socket_path
        self._socket: Optional[socket.socket] = None

    def start(self) -> None:
        """Start listening on the Unix socket."""
        # Remove existing socket file if present
        if os.path.exists(self._socket_path):
            os.unlink(self._socket_path)

        self._socket = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        self._socket.bind(self._socket_path)
        self._socket.listen(1)

    def accept(self) -> socket.socket:
        """Accept a connection (blocking)."""
        if self._socket is None:
            raise ValueError("Server not started")

        client, _ = self._socket.accept()
        return client

    async def accept_async(self) -> socket.socket:
        """Accept a connection asynchronously."""
        if self._socket is None:
            raise ValueError("Server not started")

        loop = asyncio.get_event_loop()
        self._socket.setblocking(False)
        client, _ = await loop.sock_accept(self._socket)
        return client

    def stop(self) -> None:
        """Stop the server and clean up the socket file."""
        if self._socket:
            self._socket.close()
            self._socket = None

        if os.path.exists(self._socket_path):
            os.unlink(self._socket_path)

    def __enter__(self) -> "UnixSocketServer":
        """Context manager entry."""
        self.start()
        return self

    def __exit__(self, *args: object) -> None:
        """Context manager exit."""
        self.stop()
