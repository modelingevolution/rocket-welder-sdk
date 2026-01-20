"""Cross-platform transport tests for Unix sockets.

Tests interoperability between C# and Python over real transport protocols.
These tests verify that:
1. Python can read data written by C# over Unix sockets
2. C# can read data written by Python over Unix sockets
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

from rocket_welder_sdk.segmentation_result import (
    SegmentationResultReader,
    SegmentationResultWriter,
)
from rocket_welder_sdk.transport import (
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
