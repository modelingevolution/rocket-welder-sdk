"""NNG transport using pynng library.

NNG (nanomsg next generation) provides high-performance, scalable messaging patterns.
Supported patterns:
- Pub/Sub: One publisher to many subscribers
- Push/Pull: Load-balanced distribution to workers
"""

from typing import Any, Optional, cast

import pynng

from .frame_sink import IFrameSink
from .frame_source import IFrameSource


class NngFrameSink(IFrameSink):
    """
    Frame sink that publishes to NNG Pub/Sub or Push/Pull pattern.

    Each frame is sent as a single NNG message (no framing needed - NNG handles message boundaries).
    """

    def __init__(self, socket: Any, leave_open: bool = False):
        """
        Create an NNG frame sink from a socket.

        Args:
            socket: pynng socket (Publisher or Pusher)
            leave_open: If True, doesn't close socket on close
        """
        self._socket: Any = socket
        self._leave_open = leave_open
        self._closed = False

    @classmethod
    def create_publisher(cls, url: str) -> "NngFrameSink":
        """
        Create an NNG Publisher frame sink bound to the specified URL.

        Args:
            url: NNG URL (e.g., "tcp://127.0.0.1:5555", "ipc:///tmp/mysocket")

        Returns:
            Frame sink ready to publish messages
        """
        socket = pynng.Pub0()
        socket.listen(url)
        return cls(socket, leave_open=False)

    @classmethod
    def create_pusher(cls, url: str, bind_mode: bool = True) -> "NngFrameSink":
        """
        Create an NNG Pusher frame sink.

        Args:
            url: NNG URL (e.g., "tcp://127.0.0.1:5555", "ipc:///tmp/mysocket")
            bind_mode: If True, listens (bind); if False, dials (connect)

        Returns:
            Frame sink ready to push messages
        """
        socket = pynng.Push0()
        if bind_mode:
            socket.listen(url)
        else:
            socket.dial(url)
        return cls(socket, leave_open=False)

    def write_frame(self, frame_data: bytes) -> None:
        """Write frame to NNG socket (no length prefix - NNG handles message boundaries)."""
        if self._closed:
            raise ValueError("Cannot write to closed sink")

        self._socket.send(frame_data)

    async def write_frame_async(self, frame_data: bytes) -> None:
        """Write frame asynchronously."""
        if self._closed:
            raise ValueError("Cannot write to closed sink")

        await self._socket.asend(frame_data)

    def flush(self) -> None:
        """Flush is a no-op for NNG (data sent immediately)."""
        pass

    async def flush_async(self) -> None:
        """Flush asynchronously is a no-op for NNG."""
        pass

    def close(self) -> None:
        """Close the NNG sink."""
        if self._closed:
            return
        self._closed = True
        if not self._leave_open:
            self._socket.close()

    async def close_async(self) -> None:
        """Close the NNG sink asynchronously."""
        self.close()


class NngFrameSource(IFrameSource):
    """
    Frame source that subscribes to NNG Pub/Sub or Pull pattern.

    Each NNG message is treated as a complete frame (no framing needed - NNG handles message boundaries).
    """

    def __init__(self, socket: Any, leave_open: bool = False):
        """
        Create an NNG frame source from a socket.

        Args:
            socket: pynng socket (Subscriber or Puller)
            leave_open: If True, doesn't close socket on close
        """
        self._socket: Any = socket
        self._leave_open = leave_open
        self._closed = False

    @classmethod
    def create_subscriber(cls, url: str, topic: bytes = b"") -> "NngFrameSource":
        """
        Create an NNG Subscriber frame source connected to the specified URL.

        Args:
            url: NNG URL (e.g., "tcp://127.0.0.1:5555", "ipc:///tmp/mysocket")
            topic: Optional topic filter (empty for all messages)

        Returns:
            Frame source ready to receive messages
        """
        socket = pynng.Sub0()
        socket.subscribe(topic)
        socket.dial(url)
        return cls(socket, leave_open=False)

    @classmethod
    def create_puller(cls, url: str, bind_mode: bool = True) -> "NngFrameSource":
        """
        Create an NNG Puller frame source.

        Args:
            url: NNG URL (e.g., "tcp://127.0.0.1:5555", "ipc:///tmp/mysocket")
            bind_mode: If True, listens (bind); if False, dials (connect)

        Returns:
            Frame source ready to pull messages
        """
        socket = pynng.Pull0()
        if bind_mode:
            socket.listen(url)
        else:
            socket.dial(url)
        return cls(socket, leave_open=False)

    @property
    def has_more_frames(self) -> bool:
        """Check if more frames available (NNG blocks waiting for messages)."""
        return not self._closed

    def read_frame(self) -> Optional[bytes]:
        """Read frame from NNG socket (blocking)."""
        if self._closed:
            return None

        try:
            return cast("bytes", self._socket.recv())
        except pynng.Closed:
            self._closed = True
            return None

    async def read_frame_async(self) -> Optional[bytes]:
        """Read frame asynchronously."""
        if self._closed:
            return None

        try:
            return cast("bytes", await self._socket.arecv())
        except pynng.Closed:
            self._closed = True
            return None

    def close(self) -> None:
        """Close the NNG source."""
        if self._closed:
            return
        self._closed = True
        if not self._leave_open:
            self._socket.close()

    async def close_async(self) -> None:
        """Close the NNG source asynchronously."""
        self.close()
