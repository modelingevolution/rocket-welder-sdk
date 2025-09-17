#!/usr/bin/env python3
"""
Integration test client for RocketWelder SDK.
Based on simple_client.py but adds support for --exit-after parameter.
"""

import argparse
import sys
import time
from datetime import datetime
from typing import Any, Callable, Union

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
        self.client = None  # Will be set by main

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
        cv2.putText(
            frame, timestamp, (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2
        )

        # Check if we should stop
        if self.exit_after > 0 and self.frame_count >= self.exit_after:
            print(f"Processed {self.frame_count} frames, stopping...")
            if self.client:
                self.client.stop()


def main() -> None:
    """Main entry point."""
    # Parse command line arguments
    parser = argparse.ArgumentParser(description="RocketWelder SDK Integration Test Client")
    parser.add_argument(
        "connection",
        nargs="?",
        help="Connection string (e.g., shm://buffer?mode=Duplex)"
    )
    parser.add_argument(
        "--exit-after",
        type=int,
        default=0,
        help="Exit after processing N frames (0 = unlimited)"
    )

    args = parser.parse_args()

    # Get connection string from args or environment
    if args.connection:
        connection_string = args.connection
    else:
        import os
        connection_string = os.environ.get("CONNECTION_STRING")
        if not connection_string:
            print("Error: No connection string provided", file=sys.stderr)
            print("Usage: integration_client.py <connection_string> [--exit-after N]", file=sys.stderr)
            sys.exit(1)

    # Create client
    client = rw.Client(connection_string)
    print(f"Connected: {client.connection}")

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
        print(f"Starting in Duplex mode, will exit after {args.exit_after} frames" if args.exit_after > 0 else "Starting in Duplex mode")
    else:
        callback = processor.process_frame_oneway
        print(f"Starting in OneWay mode, will exit after {args.exit_after} frames" if args.exit_after > 0 else "Starting in OneWay mode")

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