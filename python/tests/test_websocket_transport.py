"""
Unit tests for WebSocket transport classes.
Matches C# WebSocketFrameSink and WebSocketFrameSource behavior.
"""

from __future__ import annotations

from unittest.mock import MagicMock

import pytest

from rocket_welder_sdk.transport.websocket_transport import (
    WebSocketFrameSink,
    WebSocketFrameSource,
)


class MockWebSocket:
    """Mock WebSocket for testing."""

    def __init__(self, closed: bool = False) -> None:
        self._closed = closed
        self._messages: list[bytes] = []
        self._sent_messages: list[bytes] = []
        self._recv_index = 0
        self.state = MagicMock()
        self.state.name = "OPEN" if not closed else "CLOSED"

    @property
    def closed(self) -> bool:
        return self._closed

    async def send(self, message: bytes) -> None:
        if self._closed:
            raise RuntimeError("WebSocket is closed")
        self._sent_messages.append(message)

    async def recv(self) -> bytes:
        if self._closed:
            raise RuntimeError("WebSocket is closed")
        if self._recv_index >= len(self._messages):
            # Simulate close
            self._closed = True
            raise RuntimeError("Connection closed")
        message = self._messages[self._recv_index]
        self._recv_index += 1
        return message

    async def close(self) -> None:
        self._closed = True

    def add_message(self, message: bytes) -> None:
        """Add a message to be received."""
        self._messages.append(message)


class TestWebSocketFrameSink:
    """Test suite for WebSocketFrameSink class."""

    def test_init_with_none_raises(self) -> None:
        """Test that initializing with None raises ValueError."""
        with pytest.raises(ValueError):
            WebSocketFrameSink(None)  # type: ignore[arg-type]

    @pytest.mark.asyncio
    async def test_write_frame_async(self) -> None:
        """Test writing a frame asynchronously."""
        websocket = MockWebSocket()
        sink = WebSocketFrameSink(websocket)  # type: ignore[arg-type]

        await sink.write_frame_async(b"test frame data")

        assert len(websocket._sent_messages) == 1
        assert websocket._sent_messages[0] == b"test frame data"

    @pytest.mark.asyncio
    async def test_write_frame_async_multiple(self) -> None:
        """Test writing multiple frames asynchronously."""
        websocket = MockWebSocket()
        sink = WebSocketFrameSink(websocket)  # type: ignore[arg-type]

        await sink.write_frame_async(b"frame1")
        await sink.write_frame_async(b"frame2")
        await sink.write_frame_async(b"frame3")

        assert len(websocket._sent_messages) == 3
        assert websocket._sent_messages == [b"frame1", b"frame2", b"frame3"]

    @pytest.mark.asyncio
    async def test_write_frame_async_closed_raises(self) -> None:
        """Test writing to closed sink raises error."""
        websocket = MockWebSocket()
        sink = WebSocketFrameSink(websocket)  # type: ignore[arg-type]
        await sink.close_async()

        with pytest.raises(RuntimeError, match="closed"):
            await sink.write_frame_async(b"test")

    @pytest.mark.asyncio
    async def test_write_frame_async_closed_websocket_raises(self) -> None:
        """Test writing to closed websocket raises error."""
        websocket = MockWebSocket(closed=True)
        sink = WebSocketFrameSink(websocket)  # type: ignore[arg-type]

        with pytest.raises(RuntimeError, match="not open"):
            await sink.write_frame_async(b"test")

    @pytest.mark.asyncio
    async def test_flush_async_noop(self) -> None:
        """Test that flush is a no-op for WebSocket."""
        websocket = MockWebSocket()
        sink = WebSocketFrameSink(websocket)  # type: ignore[arg-type]

        # Should not raise
        await sink.flush_async()

    @pytest.mark.asyncio
    async def test_close_async(self) -> None:
        """Test closing the sink."""
        websocket = MockWebSocket()
        sink = WebSocketFrameSink(websocket)  # type: ignore[arg-type]

        await sink.close_async()

        assert websocket.closed is True

    @pytest.mark.asyncio
    async def test_close_async_leave_open(self) -> None:
        """Test closing with leave_open=True doesn't close WebSocket."""
        websocket = MockWebSocket()
        sink = WebSocketFrameSink(websocket, leave_open=True)  # type: ignore[arg-type]

        await sink.close_async()

        assert websocket.closed is False

    @pytest.mark.asyncio
    async def test_close_async_idempotent(self) -> None:
        """Test closing multiple times is safe."""
        websocket = MockWebSocket()
        sink = WebSocketFrameSink(websocket)  # type: ignore[arg-type]

        await sink.close_async()
        await sink.close_async()  # Should not raise

        assert websocket.closed is True

    @pytest.mark.asyncio
    async def test_context_manager_async(self) -> None:
        """Test async context manager."""
        websocket = MockWebSocket()
        async with WebSocketFrameSink(websocket) as sink:  # type: ignore[arg-type]
            await sink.write_frame_async(b"test")

        assert websocket.closed is True

    def test_flush_sync_noop(self) -> None:
        """Test that sync flush is a no-op."""
        websocket = MockWebSocket()
        sink = WebSocketFrameSink(websocket)  # type: ignore[arg-type]

        # Should not raise
        sink.flush()


class TestWebSocketFrameSource:
    """Test suite for WebSocketFrameSource class."""

    def test_init_with_none_raises(self) -> None:
        """Test that initializing with None raises ValueError."""
        with pytest.raises(ValueError):
            WebSocketFrameSource(None)  # type: ignore[arg-type]

    @pytest.mark.asyncio
    async def test_read_frame_async(self) -> None:
        """Test reading a frame asynchronously."""
        websocket = MockWebSocket()
        websocket.add_message(b"test frame data")
        source = WebSocketFrameSource(websocket)  # type: ignore[arg-type]

        frame = await source.read_frame_async()

        assert frame == b"test frame data"

    @pytest.mark.asyncio
    async def test_read_frame_async_multiple(self) -> None:
        """Test reading multiple frames asynchronously."""
        websocket = MockWebSocket()
        websocket.add_message(b"frame1")
        websocket.add_message(b"frame2")
        websocket.add_message(b"frame3")
        source = WebSocketFrameSource(websocket)  # type: ignore[arg-type]

        frame1 = await source.read_frame_async()
        frame2 = await source.read_frame_async()
        frame3 = await source.read_frame_async()

        assert frame1 == b"frame1"
        assert frame2 == b"frame2"
        assert frame3 == b"frame3"

    @pytest.mark.asyncio
    async def test_read_frame_async_closed_returns_none(self) -> None:
        """Test reading from closed connection returns None."""
        websocket = MockWebSocket()
        # No messages added, will close immediately
        source = WebSocketFrameSource(websocket)  # type: ignore[arg-type]

        frame = await source.read_frame_async()

        assert frame is None

    @pytest.mark.asyncio
    async def test_read_frame_async_closed_source_raises(self) -> None:
        """Test reading from closed source raises error."""
        websocket = MockWebSocket()
        websocket.add_message(b"test")
        source = WebSocketFrameSource(websocket)  # type: ignore[arg-type]
        await source.close_async()

        with pytest.raises(RuntimeError, match="closed"):
            await source.read_frame_async()

    @pytest.mark.asyncio
    async def test_has_more_frames_true(self) -> None:
        """Test has_more_frames when connection is open."""
        websocket = MockWebSocket()
        source = WebSocketFrameSource(websocket)  # type: ignore[arg-type]

        assert source.has_more_frames is True

    @pytest.mark.asyncio
    async def test_has_more_frames_false(self) -> None:
        """Test has_more_frames when connection is closed."""
        websocket = MockWebSocket(closed=True)
        source = WebSocketFrameSource(websocket)  # type: ignore[arg-type]

        assert source.has_more_frames is False

    @pytest.mark.asyncio
    async def test_close_async(self) -> None:
        """Test closing the source."""
        websocket = MockWebSocket()
        source = WebSocketFrameSource(websocket)  # type: ignore[arg-type]

        await source.close_async()

        assert websocket.closed is True

    @pytest.mark.asyncio
    async def test_close_async_leave_open(self) -> None:
        """Test closing with leave_open=True doesn't close WebSocket."""
        websocket = MockWebSocket()
        source = WebSocketFrameSource(websocket, leave_open=True)  # type: ignore[arg-type]

        await source.close_async()

        assert websocket.closed is False

    @pytest.mark.asyncio
    async def test_close_async_idempotent(self) -> None:
        """Test closing multiple times is safe."""
        websocket = MockWebSocket()
        source = WebSocketFrameSource(websocket)  # type: ignore[arg-type]

        await source.close_async()
        await source.close_async()  # Should not raise

        assert websocket.closed is True

    @pytest.mark.asyncio
    async def test_context_manager_async(self) -> None:
        """Test async context manager."""
        websocket = MockWebSocket()
        websocket.add_message(b"test")
        async with WebSocketFrameSource(websocket) as source:  # type: ignore[arg-type]
            frame = await source.read_frame_async()
            assert frame == b"test"

        assert websocket.closed is True


class TestWebSocketRoundtrip:
    """Test round-trip data transfer through WebSocket sink/source."""

    @pytest.mark.asyncio
    async def test_roundtrip_single_frame(self) -> None:
        """Test sending and receiving a single frame."""
        # Create a mock that shares a message queue
        messages: list[bytes] = []

        class SharedMockWebSocket:
            def __init__(self) -> None:
                self._closed = False
                self._recv_index = 0
                self.state = MagicMock()
                self.state.name = "OPEN"

            @property
            def closed(self) -> bool:
                return self._closed

            async def send(self, message: bytes) -> None:
                messages.append(message)

            async def recv(self) -> bytes:
                if self._recv_index >= len(messages):
                    self._closed = True
                    raise RuntimeError("No more messages")
                msg = messages[self._recv_index]
                self._recv_index += 1
                return msg

            async def close(self) -> None:
                self._closed = True

        ws = SharedMockWebSocket()
        sink = WebSocketFrameSink(ws)  # type: ignore[arg-type]
        source = WebSocketFrameSource(ws)  # type: ignore[arg-type]

        # Send frame through sink
        test_data = b"Hello, WebSocket!"
        await sink.write_frame_async(test_data)

        # Receive frame through source
        received = await source.read_frame_async()

        assert received == test_data

    @pytest.mark.asyncio
    async def test_roundtrip_multiple_frames(self) -> None:
        """Test sending and receiving multiple frames."""
        messages: list[bytes] = []

        class SharedMockWebSocket:
            def __init__(self) -> None:
                self._closed = False
                self._recv_index = 0
                self.state = MagicMock()
                self.state.name = "OPEN"

            @property
            def closed(self) -> bool:
                return self._closed

            async def send(self, message: bytes) -> None:
                messages.append(message)

            async def recv(self) -> bytes:
                if self._recv_index >= len(messages):
                    self._closed = True
                    raise RuntimeError("No more messages")
                msg = messages[self._recv_index]
                self._recv_index += 1
                return msg

            async def close(self) -> None:
                self._closed = True

        ws = SharedMockWebSocket()
        sink = WebSocketFrameSink(ws)  # type: ignore[arg-type]
        source = WebSocketFrameSource(ws)  # type: ignore[arg-type]

        # Send multiple frames
        frames = [b"frame1", b"frame2", b"frame3"]
        for frame in frames:
            await sink.write_frame_async(frame)

        # Receive all frames
        received = []
        for _ in range(len(frames)):
            frame = await source.read_frame_async()
            if frame:
                received.append(frame)

        assert received == frames
