"""Cross-platform transport tests for NNG and Unix sockets.

Tests interoperability between C# and Python over real transport protocols.
These tests verify that:
1. Python can read data written by C# over NNG
2. C# can read data written by Python over NNG
3. Python can read data written by C# over Unix sockets
4. C# can read data written by Python over Unix sockets
"""

import contextlib
import io
import os
import shutil
import struct
import subprocess
import tempfile
import threading
import time
from pathlib import Path
from typing import List, Optional

import numpy as np
import pytest

from rocket_welder_sdk.keypoints_protocol import KeyPointsSink
from rocket_welder_sdk.segmentation_result import (
    SegmentationResultReader,
    SegmentationResultWriter,
)
from rocket_welder_sdk.transport import (
    NngFrameSink,
    NngFrameSource,
    StreamFrameSource,
    UnixSocketFrameSink,
    UnixSocketFrameSource,
    UnixSocketServer,
)

# Path to C# scripts
SCRIPTS_DIR = Path(__file__).parent.parent.parent / "scripts"


def _has_dotnet_script() -> bool:
    """Check if dotnet-script is available."""
    return shutil.which("dotnet-script") is not None


def _run_csharp_script(
    script_name: str, args: List[str], timeout: float = 15.0
) -> Optional[subprocess.CompletedProcess[str]]:
    """Run a C# script and return the result."""
    script_path = SCRIPTS_DIR / script_name
    if not script_path.exists():
        return None

    try:
        result = subprocess.run(
            ["dotnet-script", str(script_path), *args],
            capture_output=True,
            text=True,
            timeout=timeout,
        )
        return result
    except subprocess.TimeoutExpired:
        return None
    except FileNotFoundError:
        return None


class TestNngTransportRoundTrip:
    """NNG transport round-trip tests (Python only)."""

    @pytest.fixture
    def ipc_address(self) -> str:
        """Generate a unique IPC address."""
        return f"ipc:///tmp/rocket-welder-test-{os.getpid()}-{time.time()}"

    def test_push_pull_single_frame(self, ipc_address: str) -> None:
        """Test Push/Pull pattern with single frame."""
        received_data: List[bytes] = []

        def receiver() -> None:
            source = NngFrameSource.create_puller(ipc_address, bind_mode=True)
            try:
                # Give pusher time to connect
                time.sleep(0.1)
                frame = source.read_frame()
                if frame:
                    received_data.append(frame)
            finally:
                source.close()

        # Start receiver in background
        receiver_thread = threading.Thread(target=receiver)
        receiver_thread.start()

        # Give receiver time to bind
        time.sleep(0.1)

        # Send data
        sink = NngFrameSink.create_pusher(ipc_address, bind_mode=False)
        try:
            test_data = b"Hello from Python NNG!"
            sink.write_frame(test_data)
            sink.flush()
        finally:
            sink.close()

        receiver_thread.join(timeout=5.0)

        assert len(received_data) == 1
        assert received_data[0] == b"Hello from Python NNG!"

    def test_push_pull_multiple_frames(self, ipc_address: str) -> None:
        """Test Push/Pull pattern with multiple frames."""
        received_data: List[bytes] = []
        num_frames = 5

        def receiver() -> None:
            source = NngFrameSource.create_puller(ipc_address, bind_mode=True)
            try:
                time.sleep(0.1)
                for _ in range(num_frames):
                    frame = source.read_frame()
                    if frame:
                        received_data.append(frame)
            finally:
                source.close()

        receiver_thread = threading.Thread(target=receiver)
        receiver_thread.start()

        time.sleep(0.1)

        sink = NngFrameSink.create_pusher(ipc_address, bind_mode=False)
        try:
            for i in range(num_frames):
                sink.write_frame(f"Frame {i}".encode())
        finally:
            sink.close()

        receiver_thread.join(timeout=5.0)

        assert len(received_data) == num_frames
        for i in range(num_frames):
            assert received_data[i] == f"Frame {i}".encode()

    def test_keypoints_over_nng(self, ipc_address: str) -> None:
        """Test KeyPoints protocol over NNG transport."""
        received_frames: List[bytes] = []

        def receiver() -> None:
            source = NngFrameSource.create_puller(ipc_address, bind_mode=True)
            try:
                time.sleep(0.1)
                # Receive one frame
                frame = source.read_frame()
                if frame:
                    received_frames.append(frame)
            finally:
                source.close()

        receiver_thread = threading.Thread(target=receiver)
        receiver_thread.start()

        time.sleep(0.1)

        # Create NNG sink and write keypoints
        nng_sink = NngFrameSink.create_pusher(ipc_address, bind_mode=False)
        try:
            # Use KeyPointsSink with frame_sink
            buffer = io.BytesIO()
            kp_sink = KeyPointsSink(buffer)

            with kp_sink.create_writer(frame_id=1) as writer:
                writer.append(0, 100, 200, 0.95)
                writer.append(1, 120, 190, 0.92)

            # Get the frame data (with varint length prefix)
            buffer.seek(0)
            frame_source = StreamFrameSource(buffer)
            frame_data = frame_source.read_frame()
            assert frame_data is not None

            # Send over NNG
            nng_sink.write_frame(frame_data)
        finally:
            nng_sink.close()

        receiver_thread.join(timeout=5.0)

        assert len(received_frames) == 1
        # Verify frame can be parsed
        assert len(received_frames[0]) > 8  # At least header


class TestUnixSocketTransportRoundTrip:
    """Unix socket transport round-trip tests (Python only)."""

    @pytest.fixture
    def socket_path(self) -> str:
        """Generate a unique socket path."""
        return f"/tmp/rocket-welder-test-{os.getpid()}-{time.time()}.sock"

    def test_single_frame(self, socket_path: str) -> None:
        """Test single frame over Unix socket."""
        received_data: List[bytes] = []

        def server() -> None:
            with UnixSocketServer(socket_path) as srv:
                client_sock = srv.accept()
                source = UnixSocketFrameSource(client_sock)
                try:
                    frame = source.read_frame()
                    if frame:
                        received_data.append(frame)
                finally:
                    source.close()

        server_thread = threading.Thread(target=server)
        server_thread.start()

        time.sleep(0.1)  # Give server time to start

        sink = UnixSocketFrameSink.connect(socket_path)
        try:
            test_data = b"Hello from Python Unix Socket!"
            sink.write_frame(test_data)
        finally:
            sink.close()

        server_thread.join(timeout=5.0)

        assert len(received_data) == 1
        assert received_data[0] == b"Hello from Python Unix Socket!"

    def test_multiple_frames(self, socket_path: str) -> None:
        """Test multiple frames over Unix socket."""
        received_data: List[bytes] = []
        num_frames = 5

        def server() -> None:
            with UnixSocketServer(socket_path) as srv:
                client_sock = srv.accept()
                source = UnixSocketFrameSource(client_sock)
                try:
                    for _ in range(num_frames):
                        frame = source.read_frame()
                        if frame:
                            received_data.append(frame)
                finally:
                    source.close()

        server_thread = threading.Thread(target=server)
        server_thread.start()

        time.sleep(0.1)

        sink = UnixSocketFrameSink.connect(socket_path)
        try:
            for i in range(num_frames):
                sink.write_frame(f"Frame {i}".encode())
        finally:
            sink.close()

        server_thread.join(timeout=5.0)

        assert len(received_data) == num_frames
        for i in range(num_frames):
            assert received_data[i] == f"Frame {i}".encode()

    def test_segmentation_over_unix_socket(self, socket_path: str) -> None:
        """Test Segmentation protocol over Unix socket transport."""
        received_frames: List[bytes] = []

        def server() -> None:
            with UnixSocketServer(socket_path) as srv:
                client_sock = srv.accept()
                source = UnixSocketFrameSource(client_sock)
                try:
                    frame = source.read_frame()
                    if frame:
                        received_frames.append(frame)
                finally:
                    source.close()

        server_thread = threading.Thread(target=server)
        server_thread.start()

        time.sleep(0.1)

        # Write segmentation data via Unix socket
        sink = UnixSocketFrameSink.connect(socket_path)
        try:
            # Create segmentation frame
            buffer = io.BytesIO()
            with SegmentationResultWriter(
                frame_id=42, width=1920, height=1080, stream=buffer
            ) as writer:
                points = np.array([[100, 200], [101, 201], [102, 199]], dtype=np.int32)
                writer.append(class_id=1, instance_id=1, points=points)

            # Get frame data (with varint prefix)
            buffer.seek(0)
            frame_source = StreamFrameSource(buffer)
            frame_data = frame_source.read_frame()
            assert frame_data is not None

            # Send over Unix socket
            sink.write_frame(frame_data)
        finally:
            sink.close()

        server_thread.join(timeout=5.0)

        assert len(received_frames) == 1

        # Verify frame can be parsed
        reader = SegmentationResultReader(io.BytesIO(received_frames[0]))
        assert reader.metadata.frame_id == 42
        assert reader.metadata.width == 1920
        assert reader.metadata.height == 1080

        instances = reader.read_all()
        assert len(instances) == 1
        assert instances[0].class_id == 1


@pytest.mark.skipif(not _has_dotnet_script(), reason="dotnet-script not installed")
class TestCrossPlatformNng:
    """Cross-platform NNG tests between C# and Python.

    These tests spawn C# scripts as subprocesses to verify interoperability.
    """

    @pytest.fixture
    def test_dir(self) -> Path:
        """Get shared test directory."""
        test_path = Path(tempfile.gettempdir()) / "rocket-welder-test"
        test_path.mkdir(exist_ok=True)
        return test_path

    @pytest.fixture
    def nng_address(self) -> str:
        """Get NNG address for cross-platform tests."""
        return f"ipc:///tmp/rocket-welder-cross-platform-nng-{os.getpid()}"

    def test_python_pusher_csharp_puller(self, test_dir: Path, nng_address: str) -> None:
        """Test Python pushes, C# pulls over NNG."""
        result_file = test_dir / "csharp_nng_received.txt"

        # Clean up
        if result_file.exists():
            result_file.unlink()

        # Python binds (listens), C# dials (connects)
        sink = NngFrameSink.create_pusher(nng_address, bind_mode=True)

        try:
            # Start C# puller in background thread (it will dial)
            csharp_result: List[Optional[subprocess.CompletedProcess[str]]] = []

            def run_csharp_puller() -> None:
                result = _run_csharp_script(
                    "nng_puller.csx", [nng_address, str(result_file)], timeout=10.0
                )
                csharp_result.append(result)

            csharp_thread = threading.Thread(target=run_csharp_puller)
            csharp_thread.start()

            # Give C# time to connect
            time.sleep(1.0)

            # Send frames
            test_message = "Hello from Python NNG Pusher!"
            sink.write_frame(test_message.encode())
            sink.flush()

            # Wait for C# to finish
            csharp_thread.join(timeout=10.0)

        finally:
            sink.close()

        # Verify C# received the data
        assert result_file.exists(), f"C# result file not created: {result_file}"
        content = result_file.read_text()
        assert "received" in content.lower(), f"Unexpected result: {content}"
        assert test_message in content, f"Message not found in: {content}"

    def test_csharp_pusher_python_puller(self, test_dir: Path, nng_address: str) -> None:
        """Test C# pushes, Python pulls over NNG."""
        test_message = "Hello from C# NNG Pusher!"
        received_data: List[bytes] = []

        def python_puller() -> None:
            # Python dials (connects)
            source = NngFrameSource.create_puller(nng_address, bind_mode=False)
            try:
                frame = source.read_frame()
                if frame:
                    received_data.append(frame)
            finally:
                source.close()

        # Start C# pusher in background (it binds/listens)
        csharp_result: List[Optional[subprocess.CompletedProcess[str]]] = []

        def run_csharp_pusher() -> None:
            result = _run_csharp_script("nng_pusher.csx", [nng_address, test_message], timeout=10.0)
            csharp_result.append(result)

        csharp_thread = threading.Thread(target=run_csharp_pusher)
        csharp_thread.start()

        # Give C# time to bind
        time.sleep(1.0)

        # Start Python puller
        puller_thread = threading.Thread(target=python_puller)
        puller_thread.start()

        # Wait for both to complete
        csharp_thread.join(timeout=10.0)
        puller_thread.join(timeout=5.0)

        # Verify Python received the data
        assert len(received_data) == 1, f"Expected 1 frame, got {len(received_data)}"
        assert received_data[0].decode() == test_message


@pytest.mark.skipif(not _has_dotnet_script(), reason="dotnet-script not installed")
class TestCrossPlatformNngPubSub:
    """Cross-platform NNG Pub/Sub tests between C# and Python."""

    @pytest.fixture
    def test_dir(self) -> Path:
        """Get shared test directory."""
        test_path = Path(tempfile.gettempdir()) / "rocket-welder-test"
        test_path.mkdir(exist_ok=True)
        return test_path

    @pytest.fixture
    def nng_address(self) -> str:
        """Get NNG address for cross-platform pub/sub tests."""
        return f"ipc:///tmp/rocket-welder-pubsub-{os.getpid()}"

    def test_python_publisher_csharp_subscriber(self, test_dir: Path, nng_address: str) -> None:
        """Test Python publishes, C# subscribes over NNG."""
        result_file = test_dir / "csharp_subscriber_received.txt"

        # Clean up
        if result_file.exists():
            result_file.unlink()

        # Python binds as publisher
        sink = NngFrameSink.create_publisher(nng_address)

        try:
            # Start C# subscriber in background (it dials)
            csharp_result: List[Optional[subprocess.CompletedProcess[str]]] = []

            def run_csharp_subscriber() -> None:
                result = _run_csharp_script(
                    "nng_subscriber.csx", [nng_address, str(result_file)], timeout=10.0
                )
                csharp_result.append(result)

            csharp_thread = threading.Thread(target=run_csharp_subscriber)
            csharp_thread.start()

            # Give C# time to start, connect, and subscribe
            # dotnet-script takes significant time to start
            time.sleep(2.0)

            # Publish message multiple times to ensure late subscriber gets it
            test_message = "Hello from Python Publisher!"
            for _ in range(3):
                sink.write_frame(test_message.encode())
                sink.flush()
                time.sleep(0.2)

            # Wait for C# to finish
            csharp_thread.join(timeout=10.0)

        finally:
            sink.close()

        # Verify C# received the data
        assert result_file.exists(), f"C# result file not created: {result_file}"
        content = result_file.read_text()
        assert "received" in content.lower(), f"Unexpected result: {content}"
        assert test_message in content, f"Message not found in: {content}"

    def test_csharp_publisher_python_subscriber(self, test_dir: Path, nng_address: str) -> None:
        """Test C# publishes, Python subscribes over NNG."""
        test_message = "Hello from C# Publisher!"
        received_data: List[bytes] = []

        def python_subscriber() -> None:
            # Python dials as subscriber with retry
            import pynng

            # Try to connect with retry - dotnet-script is slow to start
            source = None
            for _ in range(30):  # More retries for slow dotnet startup
                try:
                    socket = pynng.Sub0()
                    socket.subscribe(b"")
                    socket.recv_timeout = 5000  # 5 second timeout
                    socket.dial(nng_address)
                    source = NngFrameSource(socket, leave_open=False)
                    break
                except pynng.exceptions.ConnectionRefused:
                    time.sleep(0.3)
            if source is None:
                return

            try:
                frame = source.read_frame()
                if frame:
                    received_data.append(frame)
            except pynng.exceptions.Timeout:
                pass  # Timeout is acceptable
            finally:
                source.close()

        # Start C# publisher in background (it binds)
        csharp_result: List[Optional[subprocess.CompletedProcess[str]]] = []

        def run_csharp_publisher() -> None:
            result = _run_csharp_script(
                "nng_publisher.csx", [nng_address, test_message], timeout=20.0
            )
            csharp_result.append(result)

        csharp_thread = threading.Thread(target=run_csharp_publisher)
        csharp_thread.start()

        # Give C# time to start (dotnet-script is slow)
        time.sleep(1.0)

        # Start Python subscriber - it will retry connection
        subscriber_thread = threading.Thread(target=python_subscriber)
        subscriber_thread.start()

        # Wait for both to complete
        csharp_thread.join(timeout=20.0)
        subscriber_thread.join(timeout=10.0)

        # Verify Python received the data
        assert len(received_data) >= 1, f"Expected at least 1 frame, got {len(received_data)}"
        assert received_data[0].decode() == test_message


@pytest.mark.skipif(not _has_dotnet_script(), reason="dotnet-script not installed")
class TestCrossPlatformUnixSocket:
    """Cross-platform Unix socket tests between C# and Python.

    These tests spawn C# scripts as subprocesses to verify interoperability.
    """

    @pytest.fixture
    def test_dir(self) -> Path:
        """Get shared test directory."""
        test_path = Path(tempfile.gettempdir()) / "rocket-welder-test"
        test_path.mkdir(exist_ok=True)
        return test_path

    @pytest.fixture
    def socket_path(self) -> str:
        """Get Unix socket path for cross-platform tests."""
        return f"/tmp/rocket-welder-cross-platform-{os.getpid()}.sock"

    def test_python_server_csharp_client(self, test_dir: Path, socket_path: str) -> None:
        """Test Python Unix socket server receiving from C# client."""
        result_file = test_dir / "python_unix_received.txt"

        # Clean up
        if result_file.exists():
            result_file.unlink()
        with contextlib.suppress(OSError):
            os.unlink(socket_path)

        received_frames: List[bytes] = []
        test_message = "Hello from C# Unix Socket!"

        def server() -> None:
            with UnixSocketServer(socket_path) as srv:
                srv._socket.settimeout(10.0)  # type: ignore[union-attr]
                try:
                    client = srv.accept()
                    source = UnixSocketFrameSource(client)
                    frame = source.read_frame()
                    if frame:
                        received_frames.append(frame)
                        result_file.write_text(
                            f"received: {len(frame)} bytes, content: {frame.decode()}"
                        )
                    source.close()
                except Exception as e:
                    result_file.write_text(f"error: {e}")

        # Start Python server
        server_thread = threading.Thread(target=server)
        server_thread.start()

        # Give server time to start
        time.sleep(0.3)

        # Run C# client
        csharp_result = _run_csharp_script(
            "unix_socket_client.csx", [socket_path, test_message], timeout=10.0
        )

        server_thread.join(timeout=10.0)

        # Verify
        assert len(received_frames) == 1, f"Expected 1 frame, got {len(received_frames)}"
        assert received_frames[0].decode() == test_message
        if csharp_result:
            assert csharp_result.returncode == 0, f"C# error: {csharp_result.stderr}"

    def test_csharp_server_python_client(self, test_dir: Path, socket_path: str) -> None:
        """Test Python Unix socket client sending to C# server."""
        result_file = test_dir / "csharp_unix_received.txt"
        test_message = "Hello from Python Unix Socket!"

        # Clean up
        if result_file.exists():
            result_file.unlink()
        with contextlib.suppress(OSError):
            os.unlink(socket_path)

        # Start C# server in background
        csharp_result: List[Optional[subprocess.CompletedProcess[str]]] = []

        def run_csharp_server() -> None:
            result = _run_csharp_script(
                "unix_socket_server.csx", [socket_path, str(result_file)], timeout=15.0
            )
            csharp_result.append(result)

        csharp_thread = threading.Thread(target=run_csharp_server)
        csharp_thread.start()

        # Wait for C# server to create socket
        timeout = 5.0
        start = time.time()
        while not os.path.exists(socket_path) and (time.time() - start) < timeout:
            time.sleep(0.1)

        assert os.path.exists(socket_path), "C# server did not create socket"

        # Connect and send from Python
        sink = UnixSocketFrameSink.connect(socket_path)
        try:
            sink.write_frame(test_message.encode())
        finally:
            sink.close()

        # Wait for C# to finish
        csharp_thread.join(timeout=10.0)

        # Verify C# received the data
        assert result_file.exists(), f"C# result file not created: {result_file}"
        content = result_file.read_text()
        assert "received" in content.lower(), f"Unexpected result: {content}"
        assert test_message in content, f"Message not found in: {content}"


class TestLengthPrefixCompatibility:
    """Test that length prefix framing is compatible between C# and Python."""

    def test_length_prefix_format(self) -> None:
        """Verify 4-byte little-endian length prefix format."""
        # This is the format used by both TcpFrameSink/Source and UnixSocketFrameSink/Source

        # Test data
        frame_data = b"Test frame data for compatibility"

        # Encode as C# does: 4-byte little-endian length + data
        expected_length = len(frame_data)
        encoded = struct.pack("<I", expected_length) + frame_data

        # Verify decoding
        decoded_length = struct.unpack("<I", encoded[:4])[0]
        decoded_data = encoded[4 : 4 + decoded_length]

        assert decoded_length == expected_length
        assert decoded_data == frame_data

    def test_large_frame_length_prefix(self) -> None:
        """Test length prefix with large frame (1 MB)."""
        frame_data = b"X" * (1024 * 1024)  # 1 MB

        encoded_length = struct.pack("<I", len(frame_data))

        # Verify it's little-endian
        decoded_length = struct.unpack("<I", encoded_length)[0]
        assert decoded_length == len(frame_data)

        # Verify byte order
        decoded_big_endian = struct.unpack(">I", encoded_length)[0]
        assert decoded_big_endian != decoded_length  # Should be different


@pytest.mark.skipif(not _has_dotnet_script(), reason="dotnet-script not installed")
class TestCrossPlatformTcp:
    """Cross-platform TCP tests between C# and Python."""

    @pytest.fixture
    def test_dir(self) -> Path:
        """Get shared test directory."""
        test_path = Path(tempfile.gettempdir()) / "rocket-welder-test"
        test_path.mkdir(exist_ok=True)
        return test_path

    @pytest.fixture
    def tcp_port(self) -> int:
        """Get a free TCP port."""
        import socket as sock

        with sock.socket(sock.AF_INET, sock.SOCK_STREAM) as s:
            s.bind(("127.0.0.1", 0))
            return s.getsockname()[1]  # type: ignore[no-any-return]

    def test_python_server_csharp_client_tcp(self, test_dir: Path, tcp_port: int) -> None:
        """Test Python TCP server receiving from C# client."""
        from rocket_welder_sdk.transport import TcpFrameSource

        result_file = test_dir / "python_tcp_received.txt"
        if result_file.exists():
            result_file.unlink()

        received_frames: List[bytes] = []
        test_message = "Hello from C# TCP Client!"
        csharp_result: List[Optional[subprocess.CompletedProcess[str]]] = []

        def server() -> None:
            import socket as sock

            server_sock = sock.socket(sock.AF_INET, sock.SOCK_STREAM)
            server_sock.setsockopt(sock.SOL_SOCKET, sock.SO_REUSEADDR, 1)
            server_sock.bind(("127.0.0.1", tcp_port))
            server_sock.listen(1)
            server_sock.settimeout(15.0)  # Longer timeout for dotnet-script startup
            try:
                client, _ = server_sock.accept()
                source = TcpFrameSource(client)
                frame = source.read_frame()
                if frame:
                    received_frames.append(frame)
                source.close()
            except Exception:
                pass
            finally:
                server_sock.close()

        def run_csharp_client() -> None:
            result = _run_csharp_script(
                "tcp_client.csx", [str(tcp_port), test_message], timeout=15.0
            )
            csharp_result.append(result)

        # Start Python server first
        server_thread = threading.Thread(target=server)
        server_thread.start()

        time.sleep(0.3)  # Give server time to bind

        # Start C# client in background (dotnet-script takes time to start)
        client_thread = threading.Thread(target=run_csharp_client)
        client_thread.start()

        # Wait for both to complete
        server_thread.join(timeout=20.0)
        client_thread.join(timeout=20.0)

        assert len(received_frames) == 1, f"Expected 1 frame, got {len(received_frames)}"
        assert received_frames[0].decode() == test_message
        if csharp_result and csharp_result[0]:
            assert csharp_result[0].returncode == 0, f"C# error: {csharp_result[0].stderr}"

    def test_csharp_server_python_client_tcp(self, test_dir: Path, tcp_port: int) -> None:
        """Test Python TCP client sending to C# server."""
        from rocket_welder_sdk.transport import TcpFrameSink

        result_file = test_dir / "csharp_tcp_received.txt"
        test_message = "Hello from Python TCP Client!"

        if result_file.exists():
            result_file.unlink()

        # Start C# server in background
        csharp_result: List[Optional[subprocess.CompletedProcess[str]]] = []

        def run_csharp_server() -> None:
            result = _run_csharp_script(
                "tcp_server.csx", [str(tcp_port), str(result_file)], timeout=15.0
            )
            csharp_result.append(result)

        csharp_thread = threading.Thread(target=run_csharp_server)
        csharp_thread.start()

        # Connect and send from Python (with retry for dotnet-script startup time)
        import socket as sock

        client = None
        for _ in range(15):
            try:
                client = sock.socket(sock.AF_INET, sock.SOCK_STREAM)
                client.connect(("127.0.0.1", tcp_port))
                break
            except ConnectionRefusedError:
                client.close()
                client = None
                time.sleep(0.3)

        assert client is not None, "Could not connect to C# server"
        sink = TcpFrameSink(client)
        try:
            sink.write_frame(test_message.encode())
        finally:
            sink.close()

        csharp_thread.join(timeout=10.0)

        assert result_file.exists(), f"C# result file not created: {result_file}"
        content = result_file.read_text()
        assert "received" in content.lower(), f"Unexpected result: {content}"
        assert test_message in content, f"Message not found in: {content}"


@pytest.mark.skipif(not _has_dotnet_script(), reason="dotnet-script not installed")
class TestCrossPlatformKeyPoints:
    """Cross-platform KeyPoints protocol tests between C# and Python."""

    @pytest.fixture
    def test_dir(self) -> Path:
        """Get shared test directory."""
        test_path = Path(tempfile.gettempdir()) / "rocket-welder-test"
        test_path.mkdir(exist_ok=True)
        return test_path

    @pytest.fixture
    def nng_address(self) -> str:
        """Get NNG address for cross-platform tests."""
        return f"ipc:///tmp/rocket-welder-keypoints-{os.getpid()}"

    def test_python_writes_keypoints_csharp_reads(self, test_dir: Path, nng_address: str) -> None:
        """Test Python writes keypoints, C# reads over NNG."""
        result_file = test_dir / "csharp_keypoints_received.txt"
        if result_file.exists():
            result_file.unlink()

        # Python binds (pusher), C# dials (puller)
        sink = NngFrameSink.create_pusher(nng_address, bind_mode=True)

        try:
            # Start C# reader in background
            csharp_result: List[Optional[subprocess.CompletedProcess[str]]] = []

            def run_csharp_reader() -> None:
                result = _run_csharp_script(
                    "keypoints_reader.csx", [nng_address, str(result_file)], timeout=10.0
                )
                csharp_result.append(result)

            csharp_thread = threading.Thread(target=run_csharp_reader)
            csharp_thread.start()

            time.sleep(1.0)

            # Write keypoints frame from Python
            buffer = io.BytesIO()
            kp_sink = KeyPointsSink(buffer)
            with kp_sink.create_writer(frame_id=42) as writer:
                writer.append(0, 100, 200, 0.95)
                writer.append(1, 150, 250, 0.92)
                writer.append(2, 120, 180, 0.88)

            # Get frame data and send over NNG
            buffer.seek(0)
            frame_source = StreamFrameSource(buffer)
            frame_data = frame_source.read_frame()
            assert frame_data is not None
            sink.write_frame(frame_data)
            sink.flush()

            csharp_thread.join(timeout=10.0)

        finally:
            sink.close()

        assert result_file.exists(), f"C# result file not created: {result_file}"
        content = result_file.read_text()
        assert "frame_id=42" in content, f"Frame ID not found: {content}"
        assert "id=0" in content, f"Keypoint 0 not found: {content}"
        assert "id=1" in content, f"Keypoint 1 not found: {content}"

    def test_csharp_writes_keypoints_python_reads(self, test_dir: Path, nng_address: str) -> None:
        """Test C# writes keypoints, Python reads over NNG."""
        received_frames: List[tuple[int, list[tuple[int, int, int, float]]]] = []

        def parse_keypoints_frame(
            data: bytes,
        ) -> tuple[int, list[tuple[int, int, int, float]]]:
            """Parse raw keypoints frame data."""
            stream = io.BytesIO(data)

            # Read frame type (1 byte) - we skip it as we're only reading master frames
            _ = stream.read(1)[0]

            # Read frame ID (8 bytes, little-endian)
            frame_id = struct.unpack("<Q", stream.read(8))[0]

            # Read keypoint count (varint)
            def read_varint() -> int:
                result = 0
                shift = 0
                while True:
                    b = stream.read(1)[0]
                    result |= (b & 0x7F) << shift
                    if (b & 0x80) == 0:
                        break
                    shift += 7
                return result

            keypoint_count = read_varint()
            keypoints = []

            for _ in range(keypoint_count):
                kp_id = read_varint()
                x = struct.unpack("<i", stream.read(4))[0]
                y = struct.unpack("<i", stream.read(4))[0]
                conf_raw = struct.unpack("<H", stream.read(2))[0]
                confidence = conf_raw / 10000.0
                keypoints.append((kp_id, x, y, confidence))

            return frame_id, keypoints

        def python_reader() -> None:
            source = NngFrameSource.create_puller(nng_address, bind_mode=False)
            try:
                frame_data = source.read_frame()
                if frame_data:
                    frame_id, keypoints = parse_keypoints_frame(frame_data)
                    received_frames.append((frame_id, keypoints))
            finally:
                source.close()

        # Start C# writer (binds)
        csharp_result: List[Optional[subprocess.CompletedProcess[str]]] = []

        def run_csharp_writer() -> None:
            result = _run_csharp_script("keypoints_writer.csx", [nng_address], timeout=10.0)
            csharp_result.append(result)

        csharp_thread = threading.Thread(target=run_csharp_writer)
        csharp_thread.start()

        time.sleep(1.0)

        # Start Python reader (dials)
        reader_thread = threading.Thread(target=python_reader)
        reader_thread.start()

        csharp_thread.join(timeout=10.0)
        reader_thread.join(timeout=5.0)

        assert len(received_frames) == 1, f"Expected 1 frame, got {len(received_frames)}"
        frame_id, keypoints = received_frames[0]
        assert frame_id == 42
        assert len(keypoints) == 3


@pytest.mark.skipif(not _has_dotnet_script(), reason="dotnet-script not installed")
class TestCrossPlatformSegmentation:
    """Cross-platform Segmentation protocol tests between C# and Python."""

    @pytest.fixture
    def test_dir(self) -> Path:
        """Get shared test directory."""
        test_path = Path(tempfile.gettempdir()) / "rocket-welder-test"
        test_path.mkdir(exist_ok=True)
        return test_path

    @pytest.fixture
    def nng_address(self) -> str:
        """Get NNG address for cross-platform tests."""
        return f"ipc:///tmp/rocket-welder-segmentation-{os.getpid()}"

    def test_python_writes_segmentation_csharp_reads(
        self, test_dir: Path, nng_address: str
    ) -> None:
        """Test Python writes segmentation, C# reads over NNG."""
        result_file = test_dir / "csharp_segmentation_received.txt"
        if result_file.exists():
            result_file.unlink()

        # Python binds (pusher), C# dials (puller)
        sink = NngFrameSink.create_pusher(nng_address, bind_mode=True)

        try:
            # Start C# reader in background
            csharp_result: List[Optional[subprocess.CompletedProcess[str]]] = []

            def run_csharp_reader() -> None:
                result = _run_csharp_script(
                    "segmentation_reader.csx", [nng_address, str(result_file)], timeout=10.0
                )
                csharp_result.append(result)

            csharp_thread = threading.Thread(target=run_csharp_reader)
            csharp_thread.start()

            time.sleep(1.0)

            # Write segmentation frame from Python
            buffer = io.BytesIO()
            with SegmentationResultWriter(
                frame_id=123, width=1920, height=1080, stream=buffer
            ) as writer:
                points1 = np.array([[100, 100], [200, 100], [200, 200], [100, 200]], dtype=np.int32)
                writer.append(class_id=1, instance_id=1, points=points1)
                points2 = np.array([[300, 300], [350, 250], [400, 300]], dtype=np.int32)
                writer.append(class_id=2, instance_id=1, points=points2)

            # Get frame data and send over NNG
            buffer.seek(0)
            frame_source = StreamFrameSource(buffer)
            frame_data = frame_source.read_frame()
            assert frame_data is not None
            sink.write_frame(frame_data)
            sink.flush()

            csharp_thread.join(timeout=10.0)

        finally:
            sink.close()

        assert result_file.exists(), f"C# result file not created: {result_file}"
        content = result_file.read_text()
        assert "frame_id=123" in content, f"Frame ID not found: {content}"
        assert "width=1920" in content, f"Width not found: {content}"
        assert "class=1" in content, f"Class 1 not found: {content}"
        assert "class=2" in content, f"Class 2 not found: {content}"

    def test_csharp_writes_segmentation_python_reads(
        self, test_dir: Path, nng_address: str
    ) -> None:
        """Test C# writes segmentation, Python reads over NNG."""
        received_frames: List[tuple[int, int, int, int]] = []  # frame_id, w, h, instances

        def python_reader() -> None:
            source = NngFrameSource.create_puller(nng_address, bind_mode=False)
            try:
                frame_data = source.read_frame()
                if frame_data:
                    # Parse segmentation frame
                    reader = SegmentationResultReader(io.BytesIO(frame_data))
                    instances = reader.read_all()
                    received_frames.append(
                        (
                            reader.metadata.frame_id,
                            reader.metadata.width,
                            reader.metadata.height,
                            len(instances),
                        )
                    )
            finally:
                source.close()

        # Start C# writer (binds)
        csharp_result: List[Optional[subprocess.CompletedProcess[str]]] = []

        def run_csharp_writer() -> None:
            result = _run_csharp_script("segmentation_writer.csx", [nng_address], timeout=10.0)
            csharp_result.append(result)

        csharp_thread = threading.Thread(target=run_csharp_writer)
        csharp_thread.start()

        time.sleep(1.0)

        # Start Python reader (dials)
        reader_thread = threading.Thread(target=python_reader)
        reader_thread.start()

        csharp_thread.join(timeout=10.0)
        reader_thread.join(timeout=5.0)

        assert len(received_frames) == 1, f"Expected 1 frame, got {len(received_frames)}"
        frame_id, width, height, instance_count = received_frames[0]
        assert frame_id == 123
        assert width == 1920
        assert height == 1080
        assert instance_count == 2


@pytest.mark.skipif(not _has_dotnet_script(), reason="dotnet-script not installed")
class TestCrossPlatformMultiFrame:
    """Cross-platform multi-frame tests between C# and Python."""

    @pytest.fixture
    def nng_address(self) -> str:
        """Get NNG address for cross-platform tests."""
        return f"ipc:///tmp/rocket-welder-multi-{os.getpid()}"

    @pytest.fixture
    def test_dir(self) -> Path:
        """Get shared test directory."""
        test_path = Path(tempfile.gettempdir()) / "rocket-welder-test"
        test_path.mkdir(exist_ok=True)
        return test_path

    def test_python_sends_multiple_frames_csharp_receives(
        self, nng_address: str, test_dir: Path
    ) -> None:
        """Test Python sends multiple frames, C# receives all."""
        result_file = test_dir / "csharp_multi_received.txt"
        if result_file.exists():
            result_file.unlink()

        frame_count = 5

        # Python binds (pusher), C# dials (puller)
        sink = NngFrameSink.create_pusher(nng_address, bind_mode=True)

        try:
            # Start C# receiver in background
            csharp_result: List[Optional[subprocess.CompletedProcess[str]]] = []

            def run_csharp_receiver() -> None:
                result = _run_csharp_script(
                    "nng_multi_puller.csx",
                    [nng_address, str(frame_count), str(result_file)],
                    timeout=15.0,
                )
                csharp_result.append(result)

            csharp_thread = threading.Thread(target=run_csharp_receiver)
            csharp_thread.start()

            time.sleep(1.0)

            # Send multiple frames from Python
            for i in range(frame_count):
                sink.write_frame(f"Frame {i} from Python".encode())
                time.sleep(0.05)

            csharp_thread.join(timeout=15.0)

        finally:
            sink.close()

        assert result_file.exists(), f"C# result file not created: {result_file}"
        content = result_file.read_text()
        assert f"count={frame_count}" in content, f"Frame count mismatch: {content}"
        for i in range(frame_count):
            assert f"Frame {i} from Python" in content, f"Frame {i} not found: {content}"

    def test_csharp_sends_multiple_frames_python_receives(self, nng_address: str) -> None:
        """Test C# sends multiple frames, Python receives all."""
        frame_count = 5
        received_frames: List[bytes] = []

        def python_receiver() -> None:
            source = NngFrameSource.create_puller(nng_address, bind_mode=False)
            try:
                for _ in range(frame_count):
                    frame = source.read_frame()
                    if frame:
                        received_frames.append(frame)
            finally:
                source.close()

        # Start C# sender (binds)
        csharp_result: List[Optional[subprocess.CompletedProcess[str]]] = []

        def run_csharp_sender() -> None:
            result = _run_csharp_script(
                "nng_multi_pusher.csx", [nng_address, str(frame_count)], timeout=15.0
            )
            csharp_result.append(result)

        csharp_thread = threading.Thread(target=run_csharp_sender)
        csharp_thread.start()

        time.sleep(1.0)

        # Start Python receiver (dials)
        receiver_thread = threading.Thread(target=python_receiver)
        receiver_thread.start()

        csharp_thread.join(timeout=15.0)
        receiver_thread.join(timeout=10.0)

        assert (
            len(received_frames) == frame_count
        ), f"Expected {frame_count} frames, got {len(received_frames)}"
        for i in range(frame_count):
            assert f"Frame {i} from C#".encode() in received_frames[i]
