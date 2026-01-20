#!/usr/bin/env python3
"""
Ball detector for E2E testing.
Receives frames via shared memory, detects colored balls using OpenCV,
outputs keypoints (ball centers) and segmentation (ball contours) via sockets.
"""

import argparse
import logging
import os
import sys
import time
from dataclasses import dataclass
from typing import Any, List, Tuple

import cv2
import numpy as np
import numpy.typing as npt

import rocket_welder_sdk as rw
from rocket_welder_sdk.transport import UnixSocketFrameSink


@dataclass
class DetectedBall:
    """A detected ball with position and contour."""

    center_x: int
    center_y: int
    radius: int
    contour: npt.NDArray[np.int32]
    confidence: float


class BallDetector:
    """Detects colored balls in frames using OpenCV."""

    def __init__(
        self,
        keypoints_socket: str | None = None,
        segmentation_socket: str | None = None,
        exit_after: int = 0,
        debug: bool = False,
    ):
        self.exit_after = exit_after
        self.frame_count = 0
        self.client: rw.RocketWelderClient | None = None
        self.debug = debug
        self.logger = logging.getLogger(__name__)

        # Store socket paths for later binding (bind after client.start())
        self._keypoints_socket_path = keypoints_socket
        self._segmentation_socket_path = segmentation_socket
        self.keypoints_sink: UnixSocketFrameSink | None = None
        self.segmentation_sink: UnixSocketFrameSink | None = None
        self._sockets_bound = False

        # Timing stats
        self.processing_times: List[float] = []

    def bind_sockets(self) -> None:
        """Bind to output sockets. Call this AFTER client.start() to avoid deadlock."""
        if self._sockets_bound:
            return

        if self._keypoints_socket_path:
            self.logger.info(f"Binding keypoints socket: {self._keypoints_socket_path}")
            self.keypoints_sink = UnixSocketFrameSink.bind(self._keypoints_socket_path)
            self.logger.info("Keypoints socket connected")

        if self._segmentation_socket_path:
            self.logger.info(f"Binding segmentation socket: {self._segmentation_socket_path}")
            self.segmentation_sink = UnixSocketFrameSink.bind(self._segmentation_socket_path)
            self.logger.info("Segmentation socket connected")

        self._sockets_bound = True

    def detect_balls(self, frame: npt.NDArray[Any]) -> List[DetectedBall]:
        """Detect colored balls in the frame using HoughCircles."""
        balls: List[DetectedBall] = []

        # Convert to grayscale for circle detection
        if len(frame.shape) == 3:
            gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        else:
            gray = frame

        # Apply Gaussian blur to reduce noise
        blurred = cv2.GaussianBlur(gray, (9, 9), 2)

        # Detect circles using HoughCircles
        circles = cv2.HoughCircles(
            blurred,
            cv2.HOUGH_GRADIENT,
            dp=1,
            minDist=50,
            param1=50,
            param2=30,
            minRadius=10,
            maxRadius=200,
        )

        if circles is not None:
            circles = np.uint16(np.around(circles))
            for circle in circles[0, :]:
                center_x, center_y, radius = int(circle[0]), int(circle[1]), int(circle[2])

                # Create circular contour approximation
                contour = self._create_circle_contour(center_x, center_y, radius)

                # Calculate confidence based on circle detection quality
                confidence = min(0.95, 0.7 + (radius / 100) * 0.25)

                balls.append(
                    DetectedBall(
                        center_x=center_x,
                        center_y=center_y,
                        radius=radius,
                        contour=contour,
                        confidence=confidence,
                    )
                )

        return balls

    def _create_circle_contour(
        self, cx: int, cy: int, radius: int, num_points: int = 32
    ) -> npt.NDArray[np.int32]:
        """Create a circular contour as polygon points."""
        angles = np.linspace(0, 2 * np.pi, num_points, endpoint=False)
        points = np.column_stack(
            [
                cx + radius * np.cos(angles),
                cy + radius * np.sin(angles),
            ]
        ).astype(np.int32)
        return points.reshape((-1, 1, 2))

    def write_keypoints(self, frame_id: int, balls: List[DetectedBall]) -> None:
        """Write ball centers as keypoints to socket."""
        if not self.keypoints_sink or not balls:
            return

        # Build keypoints frame using protocol
        # Format: [frame_id:8][is_master:1][count:varint][keypoints...]
        # Each keypoint: [id:varint][x:varint][y:varint][conf:2B]
        from rocket_welder_sdk.keypoints_protocol import KeyPointsSink

        # Create a temporary sink wrapper for this frame
        sink = KeyPointsSink(self.keypoints_sink, master_frame_interval=1)
        writer = sink.create_writer(frame_id)

        for i, ball in enumerate(balls):
            writer.append(
                keypoint_id=i,
                x=ball.center_x,
                y=ball.center_y,
                confidence=ball.confidence,
            )

        # Flush to send
        writer.flush()

    def write_segmentation(
        self, frame_id: int, width: int, height: int, balls: List[DetectedBall]
    ) -> None:
        """Write ball contours as segmentation to socket."""
        if not self.segmentation_sink or not balls:
            return

        from rocket_welder_sdk.segmentation_result import SegmentationResultWriter

        writer = SegmentationResultWriter(
            frame_id=frame_id,
            width=width,
            height=height,
            frame_sink=self.segmentation_sink,
        )

        for i, ball in enumerate(balls):
            # Class 1 = ball, instance_id = detection index
            points = [(int(p[0][0]), int(p[0][1])) for p in ball.contour]
            writer.append(class_id=1, instance_id=i, points=points)

        writer.flush()

    def process_frame(self, input_frame: npt.NDArray[Any], output_frame: npt.NDArray[Any]) -> None:
        """Process a frame: detect balls, write results, draw visualization."""
        start_time = time.perf_counter()
        self.frame_count += 1

        # Get frame dimensions
        height, width = input_frame.shape[:2]

        # Detect balls
        balls = self.detect_balls(input_frame)

        # Write keypoints and segmentation
        self.write_keypoints(self.frame_count, balls)
        self.write_segmentation(self.frame_count, width, height, balls)

        # Copy input to output and draw detections
        np.copyto(output_frame, input_frame)

        # Draw detected balls on output
        for ball in balls:
            # Draw circle
            cv2.circle(
                output_frame,
                (ball.center_x, ball.center_y),
                ball.radius,
                (0, 255, 0),  # Green
                2,
            )
            # Draw center point
            cv2.circle(
                output_frame,
                (ball.center_x, ball.center_y),
                3,
                (0, 0, 255),  # Red
                -1,
            )
            # Draw label
            label = f"Ball {ball.confidence:.0%}"
            cv2.putText(
                output_frame,
                label,
                (ball.center_x - 30, ball.center_y - ball.radius - 10),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.5,
                (255, 255, 255),
                1,
            )

        # Calculate processing time
        elapsed_ms = (time.perf_counter() - start_time) * 1000
        self.processing_times.append(elapsed_ms)

        self.logger.info(
            f"Frame {self.frame_count}: detected {len(balls)} balls in {elapsed_ms:.1f}ms"
        )

        # Check if we should stop
        if self.exit_after > 0 and self.frame_count >= self.exit_after:
            self.logger.info(f"Processed {self.frame_count} frames, stopping...")
            if self.client:
                self.client.stop()

    def get_stats(self) -> dict:
        """Get processing statistics."""
        if not self.processing_times:
            return {"frames": 0}

        return {
            "frames": len(self.processing_times),
            "avg_ms": sum(self.processing_times) / len(self.processing_times),
            "min_ms": min(self.processing_times),
            "max_ms": max(self.processing_times),
        }

    def close(self) -> None:
        """Close socket connections."""
        if self.keypoints_sink:
            self.keypoints_sink.close()
        if self.segmentation_sink:
            self.segmentation_sink.close()


def setup_logging(level: int = logging.INFO) -> None:
    """Configure logging."""
    logging.basicConfig(
        level=level,
        format="%(asctime)s.%(msecs)03d [%(levelname)-8s] %(name)s: %(message)s",
        datefmt="%H:%M:%S",
        stream=sys.stdout,
    )


def main() -> None:
    """Main entry point."""
    parser = argparse.ArgumentParser(description="Ball Detector for E2E Testing")
    parser.add_argument(
        "connection",
        nargs="?",
        help="Connection string (e.g., shm://buffer?mode=Duplex)",
    )
    parser.add_argument(
        "--keypoints-socket",
        help="Unix socket path for keypoints output",
    )
    parser.add_argument(
        "--segmentation-socket",
        help="Unix socket path for segmentation output",
    )
    parser.add_argument(
        "--exit-after",
        type=int,
        default=0,
        help="Exit after processing N frames (0 = unlimited)",
    )
    parser.add_argument("--debug", action="store_true", help="Enable debug logging")

    args = parser.parse_args()

    # Setup logging
    setup_logging(logging.DEBUG if args.debug else logging.INFO)
    logger = logging.getLogger(__name__)

    # Get connection string
    connection_string = args.connection or os.environ.get("CONNECTION_STRING")
    if not connection_string:
        logger.error("No connection string provided")
        sys.exit(1)

    logger.info(f"Connection: {connection_string}")
    logger.info(f"Keypoints socket: {args.keypoints_socket or 'none'}")
    logger.info(f"Segmentation socket: {args.segmentation_socket or 'none'}")
    logger.info(f"Exit after: {args.exit_after} frames")

    # Create detector
    detector = BallDetector(
        keypoints_socket=args.keypoints_socket,
        segmentation_socket=args.segmentation_socket,
        exit_after=args.exit_after,
        debug=args.debug,
    )

    try:
        # Create client
        client = rw.Client(connection_string)
        detector.client = client
        logger.info(f"Connected: {client.connection}")

        # Start processing - this creates shared memory and starts background thread
        client.start(detector.process_frame)
        logger.info("Client started, shared memory created")

        # Now bind sockets - this blocks until clients connect
        # C# Server will connect to these sockets after connecting to shared memory
        detector.bind_sockets()
        logger.info("Sockets bound, ready to process frames")

        # Wait until stopped
        while client.is_running:
            time.sleep(0.1)

        # Print stats
        stats = detector.get_stats()
        logger.info(f"Processing stats: {stats}")

    except KeyboardInterrupt:
        logger.info("Interrupted")
    except Exception as e:
        logger.error(f"Error: {e}")
        raise
    finally:
        detector.close()
        logger.info("Done")


if __name__ == "__main__":
    main()
