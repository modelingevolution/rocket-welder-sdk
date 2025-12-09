"""Tests for NNG transport implementation."""

import threading
import time
from typing import List

import pytest

# Skip all tests if pynng not available
pynng = pytest.importorskip("pynng")

# Import after pynng check - noqa needed since import is conditional
from rocket_welder_sdk.transport.nng_transport import (  # noqa: E402
    NngFrameSink,
    NngFrameSource,
)


class TestNngFrameSink:
    """Tests for NngFrameSink."""

    def test_sink_create_publisher(self) -> None:
        """Factory method should create connected publisher."""
        sink = NngFrameSink.create_publisher("tcp://127.0.0.1:15555")
        assert not sink._closed
        assert sink._socket is not None
        sink.close()

    def test_sink_create_pusher_bind(self) -> None:
        """Factory method should create pusher in bind mode."""
        sink = NngFrameSink.create_pusher("tcp://127.0.0.1:15556", bind_mode=True)
        assert not sink._closed
        assert sink._socket is not None
        sink.close()

    def test_sink_context_manager(self) -> None:
        """Sink should work as context manager."""
        with NngFrameSink.create_publisher("tcp://127.0.0.1:15557") as sink:
            assert not sink._closed
        assert sink._closed

    def test_sink_close_idempotent(self) -> None:
        """Multiple closes should be safe."""
        sink = NngFrameSink.create_publisher("tcp://127.0.0.1:15558")
        sink.close()
        sink.close()  # Should not raise
        assert sink._closed

    def test_sink_write_after_close_raises(self) -> None:
        """Writing to closed sink should raise ValueError."""
        sink = NngFrameSink.create_publisher("tcp://127.0.0.1:15559")
        sink.close()
        with pytest.raises(ValueError, match="closed"):
            sink.write_frame(b"test")

    def test_sink_flush_noop(self) -> None:
        """Flush should be a no-op (doesn't raise)."""
        sink = NngFrameSink.create_publisher("tcp://127.0.0.1:15560")
        sink.flush()  # Should not raise
        sink.close()


class TestNngFrameSource:
    """Tests for NngFrameSource."""

    def test_source_create_subscriber(self) -> None:
        """Factory method should create connected subscriber."""
        # Need a publisher to connect to
        with NngFrameSink.create_publisher("tcp://127.0.0.1:15561"):
            time.sleep(0.1)
            source = NngFrameSource.create_subscriber("tcp://127.0.0.1:15561")
            assert not source._closed
            assert source._socket is not None
            source.close()

    def test_source_create_puller(self) -> None:
        """Factory method should create puller in bind mode."""
        source = NngFrameSource.create_puller("tcp://127.0.0.1:15562", bind_mode=True)
        assert not source._closed
        assert source._socket is not None
        source.close()

    def test_source_context_manager(self) -> None:
        """Source should work as context manager."""
        with NngFrameSink.create_publisher("tcp://127.0.0.1:15563"):
            time.sleep(0.1)
            with NngFrameSource.create_subscriber("tcp://127.0.0.1:15563") as source:
                assert not source._closed
            assert source._closed

    def test_source_has_more_frames_when_open(self) -> None:
        """has_more_frames should return True when open."""
        with NngFrameSink.create_publisher("tcp://127.0.0.1:15564"):
            time.sleep(0.1)
            source = NngFrameSource.create_subscriber("tcp://127.0.0.1:15564")
            assert source.has_more_frames
            source.close()
            assert not source.has_more_frames

    def test_source_close_idempotent(self) -> None:
        """Multiple closes should be safe."""
        with NngFrameSink.create_publisher("tcp://127.0.0.1:15565"):
            time.sleep(0.1)
            source = NngFrameSource.create_subscriber("tcp://127.0.0.1:15565")
            source.close()
            source.close()  # Should not raise
            assert source._closed

    def test_source_read_after_close_returns_none(self) -> None:
        """Reading from closed source should return None."""
        with NngFrameSink.create_publisher("tcp://127.0.0.1:15566"):
            time.sleep(0.1)
            source = NngFrameSource.create_subscriber("tcp://127.0.0.1:15566")
            source.close()
            assert source.read_frame() is None


class TestNngTransportIntegration:
    """Integration tests for NNG sink and source together."""

    # NNG pub/sub requires time for the subscriber to connect before messages
    # are published. This is the "slow subscriber" problem inherent to pub/sub.
    PUB_SUB_SETTLE_TIME = 0.5

    def test_single_frame_roundtrip(self) -> None:
        """Single frame should be sent and received correctly."""
        test_data = b"Hello, NNG!"
        received: List[bytes] = []

        with NngFrameSink.create_publisher("tcp://127.0.0.1:15570") as sink:
            time.sleep(self.PUB_SUB_SETTLE_TIME)

            with NngFrameSource.create_subscriber("tcp://127.0.0.1:15570") as source:
                time.sleep(self.PUB_SUB_SETTLE_TIME)

                sink.write_frame(test_data)

                # Set recv_timeout on socket
                source._socket.recv_timeout = 2000
                frame = source.read_frame()
                if frame:
                    received.append(frame)

        assert len(received) == 1
        assert received[0] == test_data

    def test_multiple_frames_roundtrip(self) -> None:
        """Multiple frames should be sent and received in order."""
        frames_to_send = [b"frame1", b"frame2", b"frame3"]
        received: List[bytes] = []

        with NngFrameSink.create_publisher("tcp://127.0.0.1:15571") as sink:
            time.sleep(self.PUB_SUB_SETTLE_TIME)

            with NngFrameSource.create_subscriber("tcp://127.0.0.1:15571") as source:
                time.sleep(self.PUB_SUB_SETTLE_TIME)
                source._socket.recv_timeout = 2000

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

        with NngFrameSink.create_publisher("tcp://127.0.0.1:15572") as sink:
            time.sleep(self.PUB_SUB_SETTLE_TIME)

            with NngFrameSource.create_subscriber("tcp://127.0.0.1:15572") as source:
                time.sleep(self.PUB_SUB_SETTLE_TIME)
                source._socket.recv_timeout = 5000

                sink.write_frame(large_data)
                received = source.read_frame()

        assert received == large_data

    @pytest.mark.skip(reason="pynng doesn't handle empty messages - NNG protocol limitation")
    def test_empty_frame_roundtrip(self) -> None:
        """Empty frames should be handled correctly."""
        with NngFrameSink.create_publisher("tcp://127.0.0.1:15573") as sink:
            time.sleep(self.PUB_SUB_SETTLE_TIME)

            with NngFrameSource.create_subscriber("tcp://127.0.0.1:15573") as source:
                time.sleep(self.PUB_SUB_SETTLE_TIME)
                source._socket.recv_timeout = 2000

                sink.write_frame(b"")
                received = source.read_frame()

        assert received == b""

    def test_binary_data_roundtrip(self) -> None:
        """Binary data with all byte values should roundtrip correctly."""
        binary_data = bytes(range(256))

        with NngFrameSink.create_publisher("tcp://127.0.0.1:15574") as sink:
            time.sleep(self.PUB_SUB_SETTLE_TIME)

            with NngFrameSource.create_subscriber("tcp://127.0.0.1:15574") as source:
                time.sleep(self.PUB_SUB_SETTLE_TIME)
                source._socket.recv_timeout = 2000

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
                source._socket.recv_timeout = 2000
                for _ in range(frame_count):
                    frame = source.read_frame()
                    if frame:
                        received.append(frame)
            except Exception as e:
                errors.append(e)

        with NngFrameSink.create_publisher("tcp://127.0.0.1:15575") as sink:
            time.sleep(self.PUB_SUB_SETTLE_TIME)

            with NngFrameSource.create_subscriber("tcp://127.0.0.1:15575") as source:
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

    PUB_SUB_SETTLE_TIME = 0.5

    def test_ipc_roundtrip(self) -> None:
        """IPC transport should work for local communication."""
        ipc_url = "ipc:///tmp/test_nng_roundtrip.ipc"
        test_data = b"IPC test data"

        with NngFrameSink.create_publisher(ipc_url) as sink:
            time.sleep(self.PUB_SUB_SETTLE_TIME)

            with NngFrameSource.create_subscriber(ipc_url) as source:
                time.sleep(self.PUB_SUB_SETTLE_TIME)
                source._socket.recv_timeout = 2000

                sink.write_frame(test_data)
                received = source.read_frame()

        assert received == test_data


class TestNngPushPull:
    """Tests for Push/Pull pattern."""

    def test_push_pull_roundtrip(self) -> None:
        """Push/Pull pattern should work correctly."""
        test_data = b"Push/Pull test"

        # Puller binds, pusher dials
        with NngFrameSource.create_puller("tcp://127.0.0.1:15580", bind_mode=True) as puller:
            time.sleep(0.1)

            with NngFrameSink.create_pusher("tcp://127.0.0.1:15580", bind_mode=False) as pusher:
                time.sleep(0.1)
                puller._socket.recv_timeout = 2000

                pusher.write_frame(test_data)
                received = puller.read_frame()

        assert received == test_data
