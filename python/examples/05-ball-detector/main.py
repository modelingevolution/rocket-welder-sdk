#!/usr/bin/env python3
"""
Simple example detecting a ball from videotestsrc pattern=ball.
Outputs ball edge as segmentation, center as keypoint, and position as text overlay.

This is a SINK-ONLY example - it does NOT modify the output frame.
Data is streamed via Unix sockets for downstream consumers.

Optional configuration (uses NullSink if not set):
  - SEGMENTATION_SINK_URL
  - KEYPOINTS_SINK_URL
  - GRAPHICS_SINK_URL
"""

import argparse
import logging
import os
import signal
import sys
import threading
import time
from dataclasses import dataclass
from typing import Any, List, Optional, Tuple

import cv2
import numpy as np
import numpy.typing as npt

import rocket_welder_sdk as rw
from rocket_welder_sdk import RgbColor

# Constants matching C#
BALL_CLASS_ID = 1
CENTER_KEYPOINT_ID = 0


@dataclass
class BallDetection:
    """Result of ball detection."""

    contour: Optional[npt.NDArray[np.int32]]
    center: Optional[Tuple[int, int]]
    confidence: float


def detect_ball(frame: npt.NDArray[Any]) -> BallDetection:
    """
    Detect ball contour and center from frame.

    Returns:
        BallDetection with contour, center, and confidence. Null if no ball found.
    """
    # Convert to grayscale
    if len(frame.shape) == 3:
        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    else:
        gray = frame

    # Threshold to find bright ball
    _, thresh = cv2.threshold(gray, 200, 255, cv2.THRESH_BINARY)

    # Find contours
    contours, _ = cv2.findContours(thresh, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

    if not contours:
        return BallDetection(contour=None, center=None, confidence=0.0)

    # Get largest contour (the ball)
    largest = max(contours, key=cv2.contourArea)
    area = cv2.contourArea(largest)

    if area < 100:  # Too small, likely noise
        return BallDetection(contour=None, center=None, confidence=0.0)

    # Calculate center using moments
    moments = cv2.moments(largest)
    center: Optional[Tuple[int, int]] = None
    confidence = 0.0

    if moments["m00"] > 0:
        cx = int(moments["m10"] / moments["m00"])
        cy = int(moments["m01"] / moments["m00"])
        center = (cx, cy)
        confidence = min(1.0, area / 10000)

    return BallDetection(contour=largest, center=center, confidence=confidence)


class BallDetectionService:
    """
    Detects a ball from videotestsrc pattern=ball.
    Matches C# BallDetectionService behavior exactly.
    """

    def __init__(self, client: rw.RocketWelderClient, exit_after: int = -1):
        self._client = client
        self._exit_after = exit_after
        self._frame_count = 0
        self._seg_written = 0
        self._key_written = 0
        self._logger = logging.getLogger(__name__)
        self._stop_event = threading.Event()

    def run(self, cancellation_token: Optional[threading.Event] = None) -> None:
        """Run the ball detection service."""
        self._logger.info("Starting Ball Detection client (SINK-ONLY): %s", self._client.connection)

        # Log sink configuration (NullSink used if not configured)
        seg_url = os.environ.get("SEGMENTATION_SINK_URL")
        kp_url = os.environ.get("KEYPOINTS_SINK_URL")
        gfx_url = os.environ.get("GRAPHICS_SINK_URL")

        self._logger.info("Segmentation sink: %s", seg_url or "(NullSink)")
        self._logger.info("Keypoints sink: %s", kp_url or "(NullSink)")
        self._logger.info("Graphics sink: %s", gfx_url or "(NullSink)")

        # Check connection mode and use appropriate method
        from rocket_welder_sdk.connection_string import ConnectionMode

        if self._client.connection.connection_mode == ConnectionMode.DUPLEX:
            self._logger.info("Running in DUPLEX mode (with output frame)")
            self._client.start_with_writers(self._process_frame_duplex, cancellation_token)
        else:
            self._logger.info("Running in ONE-WAY mode (sink-only, no output frame)")
            self._client.start_with_writers_oneway(self._process_frame_oneway, cancellation_token)

        if self._exit_after > 0:
            self._logger.info("Will exit after %d frames", self._exit_after)

        # Wait until stopped
        while self._client.is_running and not self._stop_event.is_set():
            time.sleep(0.1)

        self._logger.info("Stopping client... Total frames: %d", self._frame_count)
        self._client.stop()

    def _process_frame_oneway(
        self,
        input_frame: npt.NDArray[Any],
        seg_writer: rw.ISegmentationResultWriter,
        kp_writer: rw.IKeyPointsWriter,
        stage_writer: rw.IStageWriter,
    ) -> None:
        """Process frame in ONE-WAY mode (no output frame)."""
        self._process_frame_common(input_frame, seg_writer, kp_writer, stage_writer)

    def _process_frame_duplex(
        self,
        input_frame: npt.NDArray[Any],
        seg_writer: rw.ISegmentationResultWriter,
        kp_writer: rw.IKeyPointsWriter,
        stage_writer: rw.IStageWriter,
        output_frame: npt.NDArray[Any],
    ) -> None:
        """Process frame in DUPLEX mode (with output frame)."""
        self._process_frame_common(input_frame, seg_writer, kp_writer, stage_writer)
        # NOTE: We do NOT modify output - this is a sink-only example
        # But we could copy input to output if needed: output_frame[:] = input_frame

    def _process_frame_common(
        self,
        input_frame: npt.NDArray[Any],
        seg_writer: rw.ISegmentationResultWriter,
        kp_writer: rw.IKeyPointsWriter,
        stage_writer: rw.IStageWriter,
    ) -> None:
        """Common frame processing logic for both modes."""
        self._frame_count += 1

        # Detect ball
        detection = detect_ball(input_frame)

        # Write segmentation data (contour) if ball found
        if detection.contour is not None and len(detection.contour) >= 3:
            # Convert contour to list of tuples for the writer
            points = [(int(p[0][0]), int(p[0][1])) for p in detection.contour]
            seg_writer.append(BALL_CLASS_ID, 0, points)
            self._seg_written += 1

        # Write keypoint data (center) if found
        if detection.center is not None:
            kp_writer.append(
                CENTER_KEYPOINT_ID,
                detection.center[0],
                detection.center[1],
                detection.confidence,
            )
            self._key_written += 1

        # Draw ball position as text overlay in upper left corner
        layer = stage_writer[0]
        layer.set_font_size(24)
        layer.set_font_color(RgbColor(255, 255, 255))  # White text for visibility
        if detection.center is not None:
            layer.draw_text(f"Ball: ({detection.center[0]}, {detection.center[1]})", 10, 30)
        else:
            layer.draw_text("Ball: not detected", 10, 30)

        # Log every 30 frames
        if self._frame_count % 30 == 0:
            if detection.center is not None:
                self._logger.info(
                    "Frame %d: Ball at (%d, %d), confidence: %.2f, "
                    "Segmentations written: %d, Keypoints written: %d",
                    self._frame_count,
                    detection.center[0],
                    detection.center[1],
                    detection.confidence,
                    self._seg_written,
                    self._key_written,
                )
            else:
                self._logger.info("Frame %d: No ball detected", self._frame_count)

        self._check_exit()

    def _check_exit(self) -> None:
        """Check if we should exit."""
        if self._exit_after > 0 and self._frame_count >= self._exit_after:
            self._logger.info("Reached %d frames, exiting...", self._exit_after)
            self._stop_event.set()


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
    print("========================================")
    print("RocketWelder SDK Ball Detection Example")
    print("(SINK-ONLY - no frame modification)")
    print("========================================")
    print(f"Arguments received: {len(sys.argv) - 1}")
    for i, arg in enumerate(sys.argv[1:], 1):
        print(f"  [{i}]: {arg}")
    print("========================================")
    print()

    parser = argparse.ArgumentParser(description="Ball Detector for E2E Testing")
    parser.add_argument(
        "connection",
        nargs="?",
        help="Connection string (e.g., shm://buffer?mode=Duplex)",
    )
    parser.add_argument(
        "--exit-after",
        type=int,
        default=-1,
        help="Exit after processing N frames (-1 = unlimited)",
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

    logger.info("Connection: %s", connection_string)

    # Create client
    client = rw.Client(connection_string)
    logger.info("Connected: %s", client.connection)

    # Create service
    service = BallDetectionService(client, exit_after=args.exit_after)

    # Handle SIGINT/SIGTERM gracefully
    stop_event = threading.Event()

    def signal_handler(signum: int, frame: Any) -> None:
        logger.info("Received signal %d, stopping...", signum)
        stop_event.set()

    signal.signal(signal.SIGINT, signal_handler)
    signal.signal(signal.SIGTERM, signal_handler)

    try:
        service.run(stop_event)
    except Exception as e:
        logger.error("Error: %s", e)
        raise
    finally:
        client.stop()
        logger.info("Done")


if __name__ == "__main__":
    main()
