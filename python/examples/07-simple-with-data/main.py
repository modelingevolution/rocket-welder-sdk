#!/usr/bin/env python3
"""
Simple example detecting a ball from videotestsrc pattern=ball.
Outputs ball edge as segmentation and center as keypoint via NNG.

This is a SINK-ONLY example - it does NOT modify the output frame.
Data is streamed via NNG Pub/Sub for downstream consumers.

NNG publishers are auto-created by SDK when SessionId environment variable is set.
"""

from __future__ import annotations

import logging
import os
import sys
import time
from typing import TYPE_CHECKING, Any

import cv2

import rocket_welder_sdk as rw
from rocket_welder_sdk.keypoints_protocol import KeyPointsSink
from rocket_welder_sdk.segmentation_result import SegmentationResultWriter
from rocket_welder_sdk.transport import NngFrameSink

if TYPE_CHECKING:
    import numpy.typing as npt


def setup_logging() -> logging.Logger:
    """Setup logging with console output."""
    logger = logging.getLogger(__name__)
    logger.setLevel(logging.DEBUG)
    logger.handlers.clear()

    formatter = logging.Formatter(
        "%(asctime)s - %(name)s - %(levelname)s - %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S",
    )

    console_handler = logging.StreamHandler(sys.stdout)
    console_handler.setLevel(logging.INFO)
    console_handler.setFormatter(formatter)
    logger.addHandler(console_handler)

    # Configure SDK logging
    rw_logger = logging.getLogger("rocket_welder_sdk")
    rw_logger.setLevel(logging.INFO)
    rw_logger.handlers.clear()
    rw_logger.addHandler(console_handler)
    rw_logger.propagate = False

    return logger


logger: logging.Logger = None  # type: ignore


# Schema definitions
BALL_CLASS_ID = 1
CENTER_KEYPOINT_ID = 0

# Global state
frame_counter = 0
seg_sink: NngFrameSink | None = None
kp_frame_sink: NngFrameSink | None = None
kp_sink: KeyPointsSink | None = None


def detect_ball(
    frame: npt.NDArray[Any],
) -> tuple[list[tuple[int, int]] | None, tuple[int, int] | None, float]:
    """Detect ball contour and center from frame.

    Returns:
        (contour_points, center, confidence)
    """
    # Convert to grayscale (handle both color and grayscale input)
    if len(frame.shape) == 2:
        # Already grayscale
        gray = frame
    elif frame.shape[2] == 1:
        # Grayscale with channel dimension
        gray = frame[:, :, 0]
    else:
        # Color image - convert to grayscale
        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)

    # Threshold to find bright ball
    _, thresh = cv2.threshold(gray, 200, 255, cv2.THRESH_BINARY)

    # Find contours
    contours, _ = cv2.findContours(thresh, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

    if not contours:
        return None, None, 0.0

    # Get largest contour (the ball)
    largest = max(contours, key=cv2.contourArea)
    area = cv2.contourArea(largest)

    if area < 100:  # Too small, likely noise
        return None, None, 0.0

    # Get contour points as list of tuples
    contour_points = [(int(p[0][0]), int(p[0][1])) for p in largest]

    # Calculate center using moments
    moments = cv2.moments(largest)
    if moments["m00"] > 0:
        cx = int(moments["m10"] / moments["m00"])
        cy = int(moments["m01"] / moments["m00"])
        center = (cx, cy)
        confidence = min(1.0, area / 10000)  # Confidence based on area
    else:
        center = None
        confidence = 0.0

    return contour_points, center, confidence


def process_frame(input_frame: npt.NDArray[Any], output_frame: npt.NDArray[Any]) -> None:
    """Process frame: detect ball, write segmentation and keypoint data.

    NOTE: This is a SINK-ONLY example. We do NOT modify output_frame.
    Data is written to NNG sinks for downstream consumers.
    """
    global frame_counter, seg_sink, kp_sink

    # Detect ball
    contour, center, confidence = detect_ball(input_frame)

    height, width = input_frame.shape[:2]

    # Write segmentation data if ball found
    if contour and len(contour) >= 3 and seg_sink is not None:
        writer = SegmentationResultWriter(
            frame_id=frame_counter, width=width, height=height, frame_sink=seg_sink
        )
        with writer as w:
            w.append(class_id=BALL_CLASS_ID, instance_id=0, points=contour)

    # Write keypoint data if center found
    if center and kp_sink is not None:
        kp_writer = kp_sink.create_writer(frame_counter)
        with kp_writer as w:
            w.append(
                keypoint_id=CENTER_KEYPOINT_ID, x=center[0], y=center[1], confidence=confidence
            )

    # Log every 30 frames
    if frame_counter % 30 == 0:
        if center:
            logger.info("Frame %d: Ball at %s, confidence: %.2f", frame_counter, center, confidence)
        else:
            logger.info("Frame %d: No ball detected", frame_counter)

    frame_counter += 1


def main() -> None:
    """Main entry point."""
    global seg_sink, kp_frame_sink, kp_sink, logger

    # Initialize logging first
    logger = setup_logging()
    logger.info("Starting simple-with-data example (SINK-ONLY, no frame modification)")

    # Create client
    client = rw.Client.from_(sys.argv)
    logger.info("Connected: %s", client.connection)

    # Start processing - SDK auto-creates NNG publishers from SessionId env var
    # We need to set up NNG sinks BEFORE start() to avoid race condition
    # But we can't create them manually or they'll conflict with SDK's auto-creation
    # Solution: Start first, then get SDK's publishers for our wrappers
    if client.connection.connection_mode == rw.ConnectionMode.DUPLEX:
        logger.info("Running in DUPLEX mode (sink-only, no frame modification)")
        client.start(process_frame)
    else:
        logger.info("Running in ONE-WAY mode (sink-only)")

        def process_oneway(frame: npt.NDArray[Any]) -> None:
            process_frame(frame, frame)  # Second arg ignored in sink-only mode

        client.start(process_oneway)

    # Get NNG publishers created by SDK (if SessionId was set)
    # Note: First few frames may not have NNG sinks available - that's OK
    if client.nng_publishers:
        seg_sink = client.nng_publishers.get("segmentation")
        kp_frame_sink = client.nng_publishers.get("keypoints")
        if kp_frame_sink:
            kp_sink = KeyPointsSink(
                frame_sink=kp_frame_sink, master_frame_interval=300, owns_sink=False
            )
        logger.info("Using SDK's NNG publishers for segmentation and keypoints")
    else:
        logger.warning("No NNG publishers available (SessionId not set?) - data will not be streamed")

    # Run until stopped
    try:
        if client.connection.parameters.get("preview", "false").lower() == "true":
            logger.info("Showing preview... Press 'q' to stop")
            client.show()
        else:
            while client.is_running:
                time.sleep(0.1)
    except KeyboardInterrupt:
        logger.info("Stopping...")
    finally:
        client.stop()
        # NNG publishers are owned by client, no need to close manually
        logger.info("Processed %d frames", frame_counter)


if __name__ == "__main__":
    main()
