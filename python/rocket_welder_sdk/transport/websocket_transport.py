"""
WebSocket transport for reading/writing frames.
Matches C# WebSocketFrameSink and WebSocketFrameSource from RocketWelder.SDK.Transport.

Uses the websockets library for async WebSocket support.
"""

from __future__ import annotations

import asyncio
from typing import Any, Optional, Protocol

from .frame_sink import IFrameSink
from .frame_source import IFrameSource


class WebSocketProtocol(Protocol):
    """Protocol for WebSocket-like objects (for type checking)."""

    @property
    def closed(self) -> bool: ...

    @property
    def state(self) -> Any: ...

    async def send(self, message: bytes) -> None: ...

    async def recv(self) -> bytes: ...

    async def close(self) -> None: ...


class WebSocketFrameSink(IFrameSink):
    """
    Frame sink that writes to a WebSocket connection.

    Each frame is sent as a single binary WebSocket message.

    Attributes:
        leave_open: If True, doesn't close WebSocket on disposal
    """

    __slots__ = ("_closed", "_leave_open", "_websocket")

    def __init__(self, websocket: WebSocketProtocol, leave_open: bool = False) -> None:
        """
        Create a WebSocket frame sink.

        Args:
            websocket: WebSocket connection to write to
            leave_open: If True, doesn't close WebSocket on disposal
        """
        if websocket is None:
            raise ValueError("websocket cannot be None")
        self._websocket = websocket
        self._leave_open = leave_open
        self._closed = False

    def write_frame(self, frame_data: bytes) -> None:
        """
        Write a complete frame to the WebSocket synchronously.

        Note: WebSocket is inherently async, so this runs the async
        version in a new event loop. Prefer write_frame_async for better performance.

        Args:
            frame_data: Complete frame data to write

        Raises:
            RuntimeError: If sink is closed or WebSocket is not open
        """
        if self._closed:
            raise RuntimeError("WebSocketFrameSink is closed")

        # Use asyncio.run for simplicity (creates new event loop)
        try:
            loop = asyncio.get_running_loop()
            # If we're already in an async context, use run_coroutine_threadsafe
            future = asyncio.run_coroutine_threadsafe(self.write_frame_async(frame_data), loop)
            future.result()
        except RuntimeError:
            # No running event loop, create a new one
            asyncio.run(self.write_frame_async(frame_data))

    async def write_frame_async(self, frame_data: bytes) -> None:
        """
        Write a complete frame to the WebSocket asynchronously.

        Args:
            frame_data: Complete frame data to write

        Raises:
            RuntimeError: If sink is closed or WebSocket is not open
        """
        if self._closed:
            raise RuntimeError("WebSocketFrameSink is closed")

        if self._websocket.closed:
            raise RuntimeError(f"WebSocket is not open: {self._websocket.state.name}")

        # Send as single binary message
        await self._websocket.send(frame_data)

    def flush(self) -> None:
        """
        Flush any buffered data.

        WebSocket sends immediately, so this is a no-op.
        """
        pass

    async def flush_async(self) -> None:
        """
        Flush any buffered data asynchronously.

        WebSocket sends immediately, so this is a no-op.
        """
        pass

    def close(self) -> None:
        """Close the sink and release resources."""
        if self._closed:
            return
        self._closed = True

        if not self._leave_open and not self._websocket.closed:
            try:
                loop = asyncio.get_running_loop()
                future = asyncio.run_coroutine_threadsafe(self.close_async(), loop)
                future.result()
            except RuntimeError:
                asyncio.run(self._close_websocket())

    async def close_async(self) -> None:
        """Close the sink and release resources asynchronously."""
        if self._closed:
            return
        self._closed = True

        await self._close_websocket()

    async def _close_websocket(self) -> None:
        """Internal async close helper."""
        if not self._leave_open and not self._websocket.closed:
            try:  # noqa: SIM105
                await self._websocket.close()
            except Exception:
                # Best effort close
                pass


class WebSocketFrameSource(IFrameSource):
    """
    Frame source that reads from a WebSocket connection.

    Each WebSocket binary message is treated as a complete frame.

    Attributes:
        leave_open: If True, doesn't close WebSocket on disposal
    """

    __slots__ = ("_closed", "_leave_open", "_websocket")

    def __init__(self, websocket: WebSocketProtocol, leave_open: bool = False) -> None:
        """
        Create a WebSocket frame source.

        Args:
            websocket: WebSocket connection to read from
            leave_open: If True, doesn't close WebSocket on disposal
        """
        if websocket is None:
            raise ValueError("websocket cannot be None")
        self._websocket = websocket
        self._leave_open = leave_open
        self._closed = False

    @property
    def has_more_frames(self) -> bool:
        """
        Check if more frames are available.

        Returns True if the WebSocket is open or in CloseSent state.
        """
        return not self._websocket.closed

    def read_frame(self) -> Optional[bytes]:
        """
        Read a complete frame from the WebSocket synchronously.

        Note: WebSocket is inherently async, so this runs the async
        version. Prefer read_frame_async for better performance.

        Returns:
            Complete frame data, or None if connection closed

        Raises:
            RuntimeError: If source is closed
        """
        if self._closed:
            raise RuntimeError("WebSocketFrameSource is closed")

        try:
            loop = asyncio.get_running_loop()
            future = asyncio.run_coroutine_threadsafe(self.read_frame_async(), loop)
            return future.result()
        except RuntimeError:
            return asyncio.run(self.read_frame_async())

    async def read_frame_async(self) -> Optional[bytes]:
        """
        Read a complete frame from the WebSocket asynchronously.

        Returns:
            Complete frame data, or None if connection closed

        Raises:
            RuntimeError: If source is closed
            ValueError: If received non-binary message
        """
        if self._closed:
            raise RuntimeError("WebSocketFrameSource is closed")

        if not self.has_more_frames:
            return None

        try:
            # websockets library handles message framing automatically
            message = await self._websocket.recv()

            # Ensure we got binary data
            if isinstance(message, str):
                raise ValueError("Expected binary message, got text")

            return message

        except Exception:
            # Connection closed or error
            return None

    def close(self) -> None:
        """Close the source and release resources."""
        if self._closed:
            return
        self._closed = True

        if not self._leave_open and not self._websocket.closed:
            try:
                loop = asyncio.get_running_loop()
                future = asyncio.run_coroutine_threadsafe(self.close_async(), loop)
                future.result()
            except RuntimeError:
                asyncio.run(self._close_websocket())

    async def close_async(self) -> None:
        """Close the source and release resources asynchronously."""
        if self._closed:
            return
        self._closed = True

        await self._close_websocket()

    async def _close_websocket(self) -> None:
        """Internal async close helper."""
        if not self._leave_open and not self._websocket.closed:
            try:  # noqa: SIM105
                await self._websocket.close()
            except Exception:
                # Best effort close
                pass


async def connect_websocket_sink(url: str, leave_open: bool = False) -> WebSocketFrameSink:
    """
    Connect to a WebSocket server and return a frame sink.

    Args:
        url: WebSocket URL (ws:// or wss://)
        leave_open: If True, doesn't close WebSocket on disposal

    Returns:
        Connected WebSocketFrameSink
    """
    try:
        import websockets
    except ImportError as e:
        raise ImportError(
            "websockets package is required for WebSocket transport. "
            "Install with: pip install websockets"
        ) from e

    websocket = await websockets.connect(url)
    return WebSocketFrameSink(websocket, leave_open)


async def connect_websocket_source(url: str, leave_open: bool = False) -> WebSocketFrameSource:
    """
    Connect to a WebSocket server and return a frame source.

    Args:
        url: WebSocket URL (ws:// or wss://)
        leave_open: If True, doesn't close WebSocket on disposal

    Returns:
        Connected WebSocketFrameSource
    """
    try:
        import websockets
    except ImportError as e:
        raise ImportError(
            "websockets package is required for WebSocket transport. "
            "Install with: pip install websockets"
        ) from e

    websocket = await websockets.connect(url)
    return WebSocketFrameSource(websocket, leave_open)
