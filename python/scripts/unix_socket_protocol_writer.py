#!/usr/bin/env python3
"""
Python Unix Socket Protocol Writer - writes keypoints and segmentation data for C# integration tests.

Usage:
    python unix_socket_protocol_writer.py <keypoints_socket> <segmentation_socket> [--frames N]

Writes known test data:
- KeyPoints: 3 keypoints with known positions (100,200), (150,250), (120,180)
- Segmentation: 2 instances - rectangle and triangle with known vertices

This script acts as SERVER (binds) - C# connects as client.
"""

import argparse
import io
import os
import signal
import sys
import threading
import time
from pathlib import Path

# Add parent directory to path for imports
sys.path.insert(0, str(Path(__file__).parent.parent))

from rocket_welder_sdk.keypoints_protocol import KeyPointsSink
from rocket_welder_sdk.transport import UnixSocketServer

# Test data - these values are verified by C# tests
TEST_KEYPOINTS = [
    {"id": 0, "x": 100, "y": 200, "confidence": 0.95},
    {"id": 1, "x": 150, "y": 250, "confidence": 0.92},
    {"id": 2, "x": 120, "y": 180, "confidence": 0.88},
]

TEST_SEGMENTATION_INSTANCES = [
    {
        "class_id": 1,
        "instance_id": 1,
        "points": [(100, 100), (200, 100), (200, 200), (100, 200)],  # Rectangle
    },
    {
        "class_id": 2,
        "instance_id": 1,
        "points": [(300, 300), (350, 250), (400, 300)],  # Triangle
    },
]

TEST_FRAME_ID = 42
TEST_WIDTH = 1920
TEST_HEIGHT = 1080


def write_keypoints_frames(
    socket_path: str, num_frames: int, ready_event: threading.Event
) -> None:
    """Write keypoints frames to Unix socket."""
    print(f"[Python KeyPoints] Binding to {socket_path}")

    # Clean up existing socket
    if os.path.exists(socket_path):
        os.unlink(socket_path)

    with UnixSocketServer(socket_path) as server:
        ready_event.set()
        print("[Python KeyPoints] Waiting for connection...")

        client_sock = server.accept()
        print("[Python KeyPoints] Client connected!")

        # Create a frame sink from the connected socket
        from rocket_welder_sdk.transport import UnixSocketFrameSink

        # We need to wrap the accepted socket - UnixSocketFrameSink expects to connect
        # So we'll use a lower-level approach
        sink = UnixSocketFrameSink(client_sock)

        try:
            keypoints_sink = KeyPointsSink(frame_sink=sink, master_frame_interval=300)

            for frame_idx in range(num_frames):
                frame_id = TEST_FRAME_ID + frame_idx
                print(f"[Python KeyPoints] Writing frame {frame_id}...")

                writer = keypoints_sink.create_writer(frame_id)
                for kp in TEST_KEYPOINTS:
                    writer.append(
                        keypoint_id=kp["id"],
                        x=kp["x"],
                        y=kp["y"],
                        confidence=kp["confidence"],
                    )
                writer.close()

                print(f"[Python KeyPoints] Frame {frame_id} written")
                time.sleep(0.05)  # Small delay between frames

            print(f"[Python KeyPoints] Completed writing {num_frames} frames")
        finally:
            sink.close()


def write_segmentation_frames(
    socket_path: str, num_frames: int, ready_event: threading.Event
) -> None:
    """Write segmentation frames to Unix socket."""
    import struct

    print(f"[Python Segmentation] Binding to {socket_path}")

    # Clean up existing socket
    if os.path.exists(socket_path):
        os.unlink(socket_path)

    with UnixSocketServer(socket_path) as server:
        ready_event.set()
        print("[Python Segmentation] Waiting for connection...")

        client_sock = server.accept()
        print("[Python Segmentation] Client connected!")

        from rocket_welder_sdk.transport import UnixSocketFrameSink

        sink = UnixSocketFrameSink(client_sock)

        try:
            for frame_idx in range(num_frames):
                frame_id = TEST_FRAME_ID + frame_idx
                print(f"[Python Segmentation] Writing frame {frame_id}...")

                # Build raw frame data (without length prefix framing)
                buffer = io.BytesIO()

                # Frame ID (8 bytes, little-endian)
                buffer.write(struct.pack("<Q", frame_id))

                # Width and Height as varints
                _write_varint(buffer, TEST_WIDTH)
                _write_varint(buffer, TEST_HEIGHT)

                # Write instances
                for inst in TEST_SEGMENTATION_INSTANCES:
                    points = inst["points"]
                    # ClassId (1 byte), InstanceId (1 byte)
                    buffer.write(bytes([inst["class_id"], inst["instance_id"]]))
                    # Point count (varint)
                    _write_varint(buffer, len(points))

                    # Points with delta encoding
                    prev_x, prev_y = 0, 0
                    for i, (x, y) in enumerate(points):
                        if i == 0:
                            # First point: zigzag-encoded absolute values
                            _write_varint(buffer, _zigzag_encode(x))
                            _write_varint(buffer, _zigzag_encode(y))
                        else:
                            # Delta from previous point
                            dx = x - prev_x
                            dy = y - prev_y
                            _write_varint(buffer, _zigzag_encode(dx))
                            _write_varint(buffer, _zigzag_encode(dy))
                        prev_x, prev_y = x, y

                frame_data = buffer.getvalue()
                sink.write_frame(frame_data)

                print(f"[Python Segmentation] Frame {frame_id} written ({len(frame_data)} bytes)")
                time.sleep(0.05)

            print(f"[Python Segmentation] Completed writing {num_frames} frames")
        finally:
            sink.close()


def _write_varint(stream: io.BytesIO, value: int) -> None:
    """Write unsigned integer as varint."""
    while value >= 0x80:
        stream.write(bytes([value & 0x7F | 0x80]))
        value >>= 7
    stream.write(bytes([value & 0x7F]))


def _zigzag_encode(value: int) -> int:
    """ZigZag encode signed integer to unsigned."""
    return (value << 1) ^ (value >> 31)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Write keypoints and segmentation data to Unix sockets for C# integration tests"
    )
    parser.add_argument("keypoints_socket", help="Path for keypoints Unix socket")
    parser.add_argument("segmentation_socket", help="Path for segmentation Unix socket")
    parser.add_argument(
        "--frames", type=int, default=3, help="Number of frames to write (default: 3)"
    )
    parser.add_argument("--timeout", type=float, default=30.0, help="Timeout in seconds")

    args = parser.parse_args()

    print(f"[Python] Starting Unix socket protocol writer")
    print(f"[Python] KeyPoints socket: {args.keypoints_socket}")
    print(f"[Python] Segmentation socket: {args.segmentation_socket}")
    print(f"[Python] Frames to write: {args.frames}")

    # Events to signal when servers are ready
    kp_ready = threading.Event()
    seg_ready = threading.Event()

    # Start writer threads
    kp_thread = threading.Thread(
        target=write_keypoints_frames,
        args=(args.keypoints_socket, args.frames, kp_ready),
        daemon=True,
    )
    seg_thread = threading.Thread(
        target=write_segmentation_frames,
        args=(args.segmentation_socket, args.frames, seg_ready),
        daemon=True,
    )

    kp_thread.start()
    seg_thread.start()

    # Wait for both to be ready
    if not kp_ready.wait(timeout=args.timeout):
        print("[Python] ERROR: KeyPoints server failed to start")
        return 1
    if not seg_ready.wait(timeout=args.timeout):
        print("[Python] ERROR: Segmentation server failed to start")
        return 1

    print("[Python] Both servers ready, waiting for threads...")

    # Wait for completion
    kp_thread.join(timeout=args.timeout)
    seg_thread.join(timeout=args.timeout)

    # Clean up socket files
    for path in [args.keypoints_socket, args.segmentation_socket]:
        if os.path.exists(path):
            try:
                os.unlink(path)
            except OSError:
                pass

    print("[Python] Done!")
    return 0


if __name__ == "__main__":
    sys.exit(main())
