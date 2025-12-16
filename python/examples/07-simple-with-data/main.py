#!/usr/bin/env python3
"""
Simple example detecting a ball from videotestsrc pattern=ball.
Outputs ball edge as segmentation and center as keypoint.
"""

import sys
import time
from typing import Any

import cv2
import numpy as np
import numpy.typing as npt

import rocket_welder_sdk as rw
from rocket_welder_sdk.segmentation_result import SegmentationResultWriter
from rocket_welder_sdk.keypoints_protocol import KeyPointsSink
from rocket_welder_sdk.transport import StreamFrameSink
import io


# Schema definitions
BALL_CLASS_ID = 1
CENTER_KEYPOINT_ID = 0

# Global sinks for output
frame_counter = 0
seg_buffer = io.BytesIO()
kp_buffer = io.BytesIO()
kp_sink: KeyPointsSink = None  # type: ignore


def detect_ball(frame: npt.NDArray[Any]) -> tuple[list[tuple[int, int]] | None, tuple[int, int] | None, float]:
    """Detect ball contour and center from frame.

    Returns:
        (contour_points, center, confidence)
    """
    # Convert to grayscale
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
    M = cv2.moments(largest)
    if M["m00"] > 0:
        cx = int(M["m10"] / M["m00"])
        cy = int(M["m01"] / M["m00"])
        center = (cx, cy)
        confidence = min(1.0, area / 10000)  # Confidence based on area
    else:
        center = None
        confidence = 0.0

    return contour_points, center, confidence


def process_frame(input_frame: npt.NDArray[Any], output_frame: npt.NDArray[Any]) -> None:
    """Process frame: detect ball, write segmentation and keypoint data."""
    global frame_counter, kp_sink

    # Copy input to output
    np.copyto(output_frame, input_frame)

    # Detect ball
    contour, center, confidence = detect_ball(input_frame)

    height, width = input_frame.shape[:2]

    # Write segmentation data if ball found
    if contour and len(contour) >= 3:
        writer = SegmentationResultWriter(
            frame_id=frame_counter,
            width=width,
            height=height,
            stream=seg_buffer
        )
        with writer as w:
            w.append(class_id=BALL_CLASS_ID, instance_id=0, points=contour)

        # Draw contour on output
        pts = np.array(contour, dtype=np.int32)
        cv2.drawContours(output_frame, [pts], -1, (0, 255, 0), 2)

    # Write keypoint data if center found
    if center:
        kp_writer = kp_sink.create_writer(frame_counter)
        with kp_writer as w:
            w.append(keypoint_id=CENTER_KEYPOINT_ID, x=center[0], y=center[1], confidence=confidence)

        # Draw center on output
        cv2.circle(output_frame, center, 5, (0, 0, 255), -1)
        cv2.putText(output_frame, f"Center: {center}", (10, 30),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)

    frame_counter += 1


def main() -> None:
    """Main entry point."""
    global kp_sink

    print("Starting simple-with-data example...")

    # Initialize keypoints sink
    kp_frame_sink = StreamFrameSink(kp_buffer, leave_open=True)
    kp_sink = KeyPointsSink(frame_sink=kp_frame_sink, master_frame_interval=300, owns_sink=False)

    # Create client
    client = rw.Client.from_(sys.argv)
    print(f"Connected: {client.connection}")

    # Start processing (duplex mode for overlay)
    if client.connection.connection_mode == rw.ConnectionMode.DUPLEX:
        client.start(process_frame)
    else:
        # One-way mode: in-place processing
        def process_oneway(frame: npt.NDArray[Any]) -> None:
            process_frame(frame, frame)
        client.start(process_oneway)

    # Run until stopped
    try:
        if client.connection.parameters.get("preview", "false").lower() == "true":
            print("Showing preview... Press 'q' to stop")
            client.show()
        else:
            while client.is_running:
                time.sleep(0.1)
    except KeyboardInterrupt:
        print("Stopping...")
    finally:
        client.stop()
        print(f"Processed {frame_counter} frames")
        print(f"Segmentation data: {seg_buffer.tell()} bytes")
        print(f"Keypoints data: {kp_buffer.tell()} bytes")


if __name__ == "__main__":
    main()
