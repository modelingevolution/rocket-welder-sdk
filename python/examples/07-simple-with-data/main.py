#!/usr/bin/env python3
"""
Simple ball detection example using the high-level RocketWelder SDK API.

Detects a ball from videotestsrc pattern=ball and outputs:
- Ball edge as segmentation contour
- Ball center as keypoint

This example demonstrates the clean SDK interface matching C# API.
"""

from __future__ import annotations

import logging
from typing import TYPE_CHECKING

import cv2
import numpy as np

from rocket_welder_sdk.high_level import (
    IKeyPointsDataContext,
    ISegmentationDataContext,
    RocketWelderClient,
)

if TYPE_CHECKING:
    import numpy.typing as npt

    Mat = npt.NDArray[np.uint8]

# Setup logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
)
logger = logging.getLogger(__name__)


def detect_ball(
    frame: Mat,
) -> tuple[list[tuple[int, int]] | None, tuple[int, int] | None, float]:
    """Detect ball contour and center from frame."""
    # Convert to grayscale
    if len(frame.shape) == 2:
        gray = frame
    elif frame.shape[2] == 1:
        gray = frame[:, :, 0]
    else:
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

    if area < 100:
        return None, None, 0.0

    # Get contour points
    contour_points = [(int(p[0][0]), int(p[0][1])) for p in largest]

    # Calculate center
    moments = cv2.moments(largest)
    if moments["m00"] > 0:
        cx = int(moments["m10"] / moments["m00"])
        cy = int(moments["m01"] / moments["m00"])
        center = (cx, cy)
        confidence = min(1.0, area / 10000)
    else:
        center = None
        confidence = 0.0

    return contour_points, center, confidence


def main() -> None:
    """Main entry point."""
    logger.info("Starting ball detection example")

    # Create client from environment
    with RocketWelderClient.from_environment() as client:
        # Define schema - matches C# API
        ball_center = client.keypoints.define_point("ball_center")
        ball_class = client.segmentation.define_class(1, "ball")

        logger.info("Schema defined: keypoint=%s, class=%s", ball_center, ball_class)

        frame_count = 0

        def process_frame(
            input_frame: Mat,
            segmentation: ISegmentationDataContext,
            keypoints: IKeyPointsDataContext,
            output_frame: Mat,
        ) -> None:
            """Process a single frame."""
            nonlocal frame_count

            # Detect ball
            contour, center, confidence = detect_ball(input_frame)

            # Add segmentation if ball found
            if contour and len(contour) >= 3:
                segmentation.add(ball_class, instance_id=0, points=contour)

            # Add keypoint if center found
            if center:
                keypoints.add(ball_center, center[0], center[1], confidence)

            # Copy to output and draw visualization
            np.copyto(output_frame, input_frame)
            if center:
                cv2.circle(output_frame, center, 5, (0, 255, 0), -1)
            if contour:
                pts = np.array(contour, dtype=np.int32)
                cv2.polylines(output_frame, [pts], True, (0, 255, 0), 2)

            frame_count += 1
            if frame_count % 30 == 0:
                if center:
                    logger.info("Frame %d: Ball at %s", frame_count, center)
                else:
                    logger.info("Frame %d: No ball", frame_count)

        # Start processing
        try:
            client.start(process_frame)
        except NotImplementedError:
            logger.warning(
                "Video capture not yet implemented. "
                "Use low-level API with RocketWelderClient.from_(sys.argv) for now."
            )

        logger.info("Processed %d frames", frame_count)


if __name__ == "__main__":
    main()
