#!/usr/bin/env python3
"""
Integration test client for RocketWelder SDK.
Based on simple_client.py but adds support for --exit-after parameter.
"""

import argparse
import logging
import sys
import time
from datetime import datetime
from typing import Any, Callable, Optional, Union

import cv2
import numpy as np
import numpy.typing as npt

import rocket_welder_sdk as rw


class FrameProcessor:
    """Handles frame processing with counter for exit-after support."""

    def __init__(self, exit_after: int = 0):
        """
        Initialize frame processor.

        Args:
            exit_after: Number of frames to process before exiting (0 = unlimited)
        """
        self.exit_after = exit_after
        self.frame_count = 0
        self.client: Optional[rw.RocketWelderClient] = None  # Will be set by main

    def process_frame_duplex(
        self, input_frame: npt.NDArray[Any], output_frame: npt.NDArray[Any]
    ) -> None:
        """Process frame in duplex mode."""
        self.frame_count += 1
        print(f"Processed frame {self.frame_count}")

        # Copy input to output and add timestamp
        np.copyto(output_frame, input_frame)
        timestamp = datetime.now().strftime("%H:%M:%S")
        cv2.putText(
            output_frame,
            timestamp,
            (10, 30),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.7,
            (255, 255, 255),
            2,
        )

        # Check if we should stop
        if self.exit_after > 0 and self.frame_count >= self.exit_after:
            print(f"Processed {self.frame_count} frames, stopping...")
            if self.client:
                self.client.stop()

    def process_frame_oneway(self, frame: npt.NDArray[Any]) -> None:
        """Process frame in oneway mode."""
        self.frame_count += 1
        print(f"Processed frame {self.frame_count}")

        # Modify frame in-place
        timestamp = datetime.now().strftime("%H:%M:%S")
        cv2.putText(frame, timestamp, (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)

        # Check if we should stop
        if self.exit_after > 0 and self.frame_count >= self.exit_after:
            print(f"Processed {self.frame_count} frames, stopping...")
            if self.client:
                self.client.stop()


def setup_logging(level: int = logging.INFO) -> None:
    """Configure logging for debug output."""
    logging.basicConfig(
        level=level,
        format="%(asctime)s.%(msecs)03d [%(levelname)-8s] %(name)s: %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S",
        stream=sys.stdout,
    )


def main() -> None:
    """Main entry point."""
    # Parse command line arguments
    parser = argparse.ArgumentParser(description="RocketWelder SDK Integration Test Client")
    parser.add_argument(
        "connection", nargs="?", help="Connection string (e.g., shm://buffer?mode=Duplex)"
    )
    parser.add_argument(
        "--exit-after", type=int, default=0, help="Exit after processing N frames (0 = unlimited)"
    )
    parser.add_argument("--debug", action="store_true", help="Enable debug logging")

    args = parser.parse_args()

    # Setup logging if debug enabled
    if args.debug:
        setup_logging(logging.DEBUG)
        logging.getLogger("rocket_welder_sdk").setLevel(logging.DEBUG)
        logging.getLogger("zerobuffer").setLevel(logging.DEBUG)

    # Get connection string from args or environment
    if args.connection:
        connection_string = args.connection
    else:
        import os

        connection_string = os.environ.get("CONNECTION_STRING")
        if not connection_string:
            print("Error: No connection string provided", file=sys.stderr)
            print(
                "Usage: integration_client.py <connection_string> [--exit-after N]", file=sys.stderr
            )
            sys.exit(1)

    # Parse timeout from connection string for logging
    timeout_ms = 5000  # default
    if "timeout=" in connection_string:
        timeout_part = connection_string.split("timeout=")[1].split("&")[0]
        timeout_ms = int(timeout_part)

    # Log connection details
    logger = logging.getLogger(__name__)
    start_time = time.time()
    if args.debug:
        logger.info("Creating client with connection: %s", connection_string)
        logger.info("Expected timeout: %d ms (%.1f seconds)", timeout_ms, timeout_ms / 1000.0)

    # Create client
    try:
        client = rw.Client(connection_string)
        elapsed = (time.time() - start_time) * 1000
        print(f"Connected: {client.connection}")
        if args.debug:
            logger.info("Client connected successfully after %.1f ms", elapsed)
    except Exception as e:
        elapsed = (time.time() - start_time) * 1000
        print(f"Failed to connect after {elapsed:.1f} ms: {e}", file=sys.stderr)
        if args.debug:
            logger.error("Connection failed after %.1f ms: %s", elapsed, e)
            if abs(elapsed - timeout_ms) > 500:  # Allow 500ms tolerance
                logger.warning(
                    "UNEXPECTED: Timeout at %.1f ms, expected ~%d ms", elapsed, timeout_ms
                )
        sys.exit(1)

    # Create processor with exit-after support
    processor = FrameProcessor(args.exit_after)
    processor.client = client  # Give processor access to client for stopping

    # Process frames based on mode
    callback: Union[
        Callable[[npt.NDArray[Any]], None],
        Callable[[npt.NDArray[Any], npt.NDArray[Any]], None],
    ]

    if client.connection.connection_mode == rw.ConnectionMode.DUPLEX:
        callback = processor.process_frame_duplex
        print(
            f"Starting in Duplex mode, will exit after {args.exit_after} frames"
            if args.exit_after > 0
            else "Starting in Duplex mode"
        )
    else:
        callback = processor.process_frame_oneway
        print(
            f"Starting in OneWay mode, will exit after {args.exit_after} frames"
            if args.exit_after > 0
            else "Starting in OneWay mode"
        )

    # Start processing
    client.start(callback)

    # Check if preview is enabled and handle display
    try:
        if client.connection.parameters.get("preview", "false").lower() == "true":
            # Show preview - blocks until 'q' pressed or stopped
            print("Showing preview... Press 'q' to stop")
            client.show()
        else:
            # No preview, just keep running until stopped or exit-after reached
            while client.is_running:
                time.sleep(0.1)
    except KeyboardInterrupt:
        print("\nStopping...")
    finally:
        client.stop()
        print(f"Total frames processed: {processor.frame_count}")


if __name__ == "__main__":
    main()
