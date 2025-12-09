"""Tests for NNG transport implementation."""

import threading
import time
from typing import List

import pytest

# Skip all tests if pynng not available
pynng = pytest.importorskip("pynng")

from rocket_welder_sdk.transport.nng_transport import NngFrameSink, NngFrameSource


class TestNngFrameSink:
    """Tests for NngFrameSink."""

    def test_sink_initialization(self) -> None:
        """Sink should initialize without connecting."""
        sink = NngFrameSink("tcp://127.0.0.1:15555")
        assert not sink._closed
        assert sink._socket is None
        sink.close()

    def test_sink_context_manager(self) -> None:
        """Sink should work as context manager."""
        with NngFrameSink("tcp://127.0.0.1:15556") as sink:
            assert not sink._closed
        assert sink._closed

    def test_sink_write_creates_socket(self) -> None:
        """Writing should lazily create socket."""
        sink = NngFrameSink("tcp://127.0.0.1:15557")
        assert sink._socket is None
        # Force socket creation
        sink._ensure_connected()
        assert sink._socket is not None
        sink.close()

    def test_sink_close_idempotent(self) -> None:
        """Multiple closes should be safe."""
        sink = NngFrameSink("tcp://127.0.0.1:15558")
        sink._ensure_connected()
        sink.close()
        sink.close()  # Should not raise
        assert sink._closed

    def test_sink_write_after_close_raises(self) -> None:
        """Writing to closed sink should raise ValueError."""
        sink = NngFrameSink("tcp://127.0.0.1:15559")
        sink.close()
        with pytest.raises(ValueError, match="closed"):
            sink.write_frame(b"test")

    def test_sink_flush_noop(self) -> None:
        """Flush should be a no-op (doesn't raise)."""
        sink = NngFrameSink("tcp://127.0.0.1:15560")
        sink.flush()  # Should not raise
        sink.close()


class TestNngFrameSource:
    """Tests for NngFrameSource."""

    def test_source_initialization(self) -> None:
        """Source should initialize without connecting."""
        source = NngFrameSource("tcp://127.0.0.1:15561")
        assert not source._closed
        assert source._socket is None
        source.close()

    def test_source_context_manager(self) -> None:
        """Source should work as context manager."""
        # Need a sink to connect to
        with NngFrameSink("tcp://127.0.0.1:15562"):
            time.sleep(0.1)  # Let sink bind
            with NngFrameSource("tcp://127.0.0.1:15562") as source:
                assert not source._closed
            assert source._closed

    def test_source_has_more_frames_when_open(self) -> None:
        """has_more_frames should return True when open."""
        source = NngFrameSource("tcp://127.0.0.1:15563")
        assert source.has_more_frames
        source.close()
        assert not source.has_more_frames

    def test_source_close_idempotent(self) -> None:
        """Multiple closes should be safe."""
        with NngFrameSink("tcp://127.0.0.1:15564"):
            time.sleep(0.1)
            source = NngFrameSource("tcp://127.0.0.1:15564")
            source._ensure_connected()
            source.close()
            source.close()  # Should not raise
            assert source._closed

    def test_source_read_after_close_returns_none(self) -> None:
        """Reading from closed source should return None."""
        source = NngFrameSource("tcp://127.0.0.1:15565")
        source.close()
        assert source.read_frame() is None

    def test_source_read_timeout_returns_none(self) -> None:
        """Reading with no messages should timeout and return None."""
        with NngFrameSink("tcp://127.0.0.1:15566"):
            time.sleep(0.1)
            source = NngFrameSource("tcp://127.0.0.1:15566", recv_timeout_ms=100)
            result = source.read_frame()
            assert result is None
            source.close()


class TestNngTransportIntegration:
    """Integration tests for NNG sink and source together."""

    # NNG pub/sub requires time for the subscriber to connect before messages
    # are published. This is the "slow subscriber" problem inherent to pub/sub.
    PUB_SUB_SETTLE_TIME = 0.5

    def test_single_frame_roundtrip(self) -> None:
        """Single frame should be sent and received correctly."""
        test_data = b"Hello, NNG!"
        received: List[bytes] = []

        with NngFrameSink("tcp://127.0.0.1:15570") as sink:
            time.sleep(self.PUB_SUB_SETTLE_TIME)  # Let sink bind

            with NngFrameSource("tcp://127.0.0.1:15570", recv_timeout_ms=2000) as source:
                time.sleep(self.PUB_SUB_SETTLE_TIME)  # Let source connect

                sink.write_frame(test_data)
                frame = source.read_frame()
                if frame:
                    received.append(frame)

        assert len(received) == 1
        assert received[0] == test_data

    def test_multiple_frames_roundtrip(self) -> None:
        """Multiple frames should be sent and received in order."""
        frames_to_send = [b"frame1", b"frame2", b"frame3"]
        received: List[bytes] = []

        with NngFrameSink("tcp://127.0.0.1:15571") as sink:
            time.sleep(self.PUB_SUB_SETTLE_TIME)

            with NngFrameSource("tcp://127.0.0.1:15571", recv_timeout_ms=2000) as source:
                time.sleep(self.PUB_SUB_SETTLE_TIME)

                for frame_data in frames_to_send:
                    sink.write_frame(frame_data)

                for _ in range(len(frames_to_send)):
                    frame = source.read_frame()
                    if frame:
                        received.append(frame)

        assert received == frames_to_send

    def test_large_frame_roundtrip(self) -> None:
        """Large frames should be handled correctly."""
        large_data = b"x" * (1024 * 1024)  # 1 MB

        with NngFrameSink("tcp://127.0.0.1:15572") as sink:
            time.sleep(self.PUB_SUB_SETTLE_TIME)

            with NngFrameSource("tcp://127.0.0.1:15572", recv_timeout_ms=5000) as source:
                time.sleep(self.PUB_SUB_SETTLE_TIME)

                sink.write_frame(large_data)
                received = source.read_frame()

        assert received == large_data

    def test_empty_frame_roundtrip(self) -> None:
        """Empty frames should be handled correctly."""
        with NngFrameSink("tcp://127.0.0.1:15573") as sink:
            time.sleep(self.PUB_SUB_SETTLE_TIME)

            with NngFrameSource("tcp://127.0.0.1:15573", recv_timeout_ms=2000) as source:
                time.sleep(self.PUB_SUB_SETTLE_TIME)

                sink.write_frame(b"")
                received = source.read_frame()

        assert received == b""

    def test_binary_data_roundtrip(self) -> None:
        """Binary data with all byte values should roundtrip correctly."""
        binary_data = bytes(range(256))

        with NngFrameSink("tcp://127.0.0.1:15574") as sink:
            time.sleep(self.PUB_SUB_SETTLE_TIME)

            with NngFrameSource("tcp://127.0.0.1:15574", recv_timeout_ms=2000) as source:
                time.sleep(self.PUB_SUB_SETTLE_TIME)

                sink.write_frame(binary_data)
                received = source.read_frame()

        assert received == binary_data

    def test_concurrent_sender_receiver(self) -> None:
        """Concurrent sending and receiving should work."""
        frame_count = 10
        received: List[bytes] = []
        errors: List[Exception] = []

        def receiver(source: NngFrameSource) -> None:
            try:
                for _ in range(frame_count):
                    frame = source.read_frame()
                    if frame:
                        received.append(frame)
            except Exception as e:
                errors.append(e)

        with NngFrameSink("tcp://127.0.0.1:15575") as sink:
            time.sleep(self.PUB_SUB_SETTLE_TIME)

            with NngFrameSource("tcp://127.0.0.1:15575", recv_timeout_ms=2000) as source:
                time.sleep(self.PUB_SUB_SETTLE_TIME)

                recv_thread = threading.Thread(target=receiver, args=(source,))
                recv_thread.start()

                for i in range(frame_count):
                    sink.write_frame(f"frame{i}".encode())
                    time.sleep(0.01)  # Small delay between sends

                recv_thread.join(timeout=5.0)

        assert not errors, f"Receiver errors: {errors}"
        assert len(received) == frame_count


class TestNngTransportIpc:
    """Tests using IPC transport (faster for local tests)."""

    # NNG pub/sub requires time for the subscriber to connect
    PUB_SUB_SETTLE_TIME = 0.5

    def test_ipc_roundtrip(self) -> None:
        """IPC transport should work for local communication."""
        ipc_url = "ipc:///tmp/test_nng_roundtrip.ipc"
        test_data = b"IPC test data"

        with NngFrameSink(ipc_url) as sink:
            time.sleep(self.PUB_SUB_SETTLE_TIME)

            with NngFrameSource(ipc_url, recv_timeout_ms=2000) as source:
                time.sleep(self.PUB_SUB_SETTLE_TIME)

                sink.write_frame(test_data)
                received = source.read_frame()

        assert received == test_data
