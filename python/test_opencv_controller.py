#!/usr/bin/env python3
"""Test OpenCV controller with file protocol support."""

import sys
import time
from typing import Any

import cv2
import numpy as np
import numpy.typing as npt

import rocket_welder_sdk as rw


def create_test_video(filepath: str, width: int = 640, height: int = 480, fps: int = 30, frames: int = 30) -> None:
    """Create a test video file with colored frames."""
    fourcc = cv2.VideoWriter_fourcc(*"mp4v")
    out = cv2.VideoWriter(filepath, fourcc, fps, (width, height))

    for i in range(frames):
        # Create frame with changing color
        frame = np.zeros((height, width, 3), dtype=np.uint8)
        color = (int(255 * i / frames), 0, int(255 * (1 - i / frames)))
        cv2.rectangle(frame, (50, 50), (width - 50, height - 50), color, -1)

        # Add frame number
        cv2.putText(frame, f"Frame {i}", (100, 100), cv2.FONT_HERSHEY_SIMPLEX, 2, (255, 255, 255), 3)

        out.write(frame)

    out.release()
    print(f"Created test video: {filepath}")


def test_file_protocol_parsing() -> None:
    """Test file protocol connection string parsing."""
    print("\n=== Testing File Protocol Parsing ===")

    # Test basic file path
    conn = rw.ConnectionString.parse("file:///tmp/video.mp4")
    assert conn.protocol == rw.Protocol.FILE
    assert conn.file_path == "/tmp/video.mp4"
    assert conn.parameters == {}
    print(f"✓ Basic file path: {conn}")

    # Test with loop parameter
    conn = rw.ConnectionString.parse("file:///tmp/video.mp4?loop=true")
    assert conn.protocol == rw.Protocol.FILE
    assert conn.file_path == "/tmp/video.mp4"
    assert conn.parameters["loop"] == "true"
    print(f"✓ File with loop: {conn}")

    # Test with multiple parameters
    conn = rw.ConnectionString.parse("file:///home/user/test.avi?loop=false&speed=2.0")
    assert conn.protocol == rw.Protocol.FILE
    assert conn.file_path == "/home/user/test.avi"
    assert conn.parameters["loop"] == "false"
    assert conn.parameters["speed"] == "2.0"
    print(f"✓ Multiple parameters: {conn}")


def test_file_playback() -> None:
    """Test actual file playback with OpenCvController."""
    print("\n=== Testing File Playback ===")

    # Create a test video
    test_file = "/tmp/test_video.mp4"
    create_test_video(test_file, frames=10)

    # Test without loop
    print("\nTesting without loop...")
    frame_count = 0

    def process_frame(frame: npt.NDArray[Any]) -> None:
        nonlocal frame_count
        frame_count += 1
        print(f"  Received frame {frame_count}: shape={frame.shape}, dtype={frame.dtype}")

    client = rw.Client(f"file://{test_file}")
    client.start(process_frame)

    # Wait for playback to complete
    time.sleep(1.0)
    client.stop()

    print(f"✓ Received {frame_count} frames without loop")
    assert frame_count == 10, f"Expected 10 frames, got {frame_count}"

    # Test with loop
    print("\nTesting with loop...")
    frame_count = 0

    client = rw.Client(f"file://{test_file}?loop=true")
    client.start(process_frame)

    # Let it loop for a bit
    time.sleep(0.5)
    client.stop()

    print(f"✓ Received {frame_count} frames with loop (should be > 10)")
    assert frame_count > 10, f"Expected > 10 frames with loop, got {frame_count}"


def test_opencv_controller_direct() -> None:
    """Test OpenCvController directly."""
    print("\n=== Testing OpenCvController Direct ===")

    # Create test video
    test_file = "/tmp/test_direct.mp4"
    create_test_video(test_file, frames=5)

    # Create connection string
    conn = rw.ConnectionString.parse(f"file://{test_file}?loop=false")

    # Create controller
    controller = rw.OpenCvController(conn)

    frames_received = []

    def capture_frame(frame: npt.NDArray[Any]) -> None:
        frames_received.append(frame.copy())
        print(f"  Frame {len(frames_received)}: {frame.shape}")

    # Start and run
    controller.start(capture_frame)
    time.sleep(0.5)
    controller.stop()

    print(f"✓ Controller received {len(frames_received)} frames")
    assert len(frames_received) == 5, f"Expected 5 frames, got {len(frames_received)}"


def test_error_handling() -> None:
    """Test error handling for non-existent files."""
    print("\n=== Testing Error Handling ===")

    try:
        client = rw.Client("file:///nonexistent/video.mp4")
        client.start(lambda frame: None)
        assert False, "Should have raised FileNotFoundError"
    except FileNotFoundError as e:
        print(f"✓ Correctly raised FileNotFoundError: {e}")

    try:
        conn = rw.ConnectionString.parse("unsupported://test")
        assert False, "Should have raised ValueError for unsupported protocol"
    except ValueError as e:
        print(f"✓ Correctly raised ValueError for unsupported protocol: {e}")


def main() -> None:
    """Run all tests."""
    print("=" * 60)
    print("Testing OpenCV Controller with File Protocol")
    print("=" * 60)

    test_file_protocol_parsing()
    test_file_playback()
    test_opencv_controller_direct()
    test_error_handling()

    print("\n" + "=" * 60)
    print("All tests passed! ✓")
    print("=" * 60)


if __name__ == "__main__":
    main()