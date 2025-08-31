#!/usr/bin/env python3
"""
RocketWelder SDK SimpleClient - Python Version
High-performance video streaming client using ZeroBuffer shared memory.
"""

import argparse
import asyncio
import logging
import os
import signal
import sys
from datetime import datetime
from typing import Optional

import cv2
import numpy as np

# Add parent directory to path for imports
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from rocket_welder_sdk import ConnectionMode, RocketWelderClient


class VideoProcessingService:
    """Service for processing video frames from shared memory."""

    def __init__(
        self,
        client: RocketWelderClient,
        exit_after: int = -1,
        logger: Optional[logging.Logger] = None,
    ):
        """
        Initialize the video processing service.

        Args:
            client: RocketWelderClient instance
            exit_after: Number of frames to process before exiting (-1 for unlimited)
            logger: Optional logger instance
        """
        self.client = client
        self.exit_after = exit_after
        self.logger = logger or logging.getLogger(__name__)
        self.frame_count = 0
        self.stop_event = asyncio.Event()
        self._loop: Optional[asyncio.AbstractEventLoop] = None

    async def run(self) -> None:
        """Run the video processing service."""
        self.logger.info(f"Starting RocketWelder client... {self.client.connection}")
        
        # Store the event loop for use in callbacks
        self._loop = asyncio.get_running_loop()

        # Check if we're in duplex mode or one-way mode
        if self.client.connection.connection_mode == ConnectionMode.DUPLEX:
            self.logger.info("Running in DUPLEX mode - will process frames and return results")
            self.logger.info(
                f"Can be tested with:\n\n\t"
                f"gst-launch-1.0 videotestsrc num-buffers={self.exit_after} pattern=ball ! "
                f"video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! "
                f"zerofilter channel-name={self.client.connection.buffer_name} ! fakesink"
            )
            callback = self.process_frame_duplex
        else:
            self.logger.info("Running in ONE-WAY mode - will receive and process frames in-place")
            self.logger.info(
                f"Can be tested with:\n\n\t"
                f"gst-launch-1.0 videotestsrc num-buffers={self.exit_after} pattern=ball ! "
                f"video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! "
                f"zerosink buffer-name={self.client.connection.buffer_name} sync=false"
            )
            callback = self.process_frame_oneway

        if self.exit_after > 0:
            self.logger.info(f"Will exit after {self.exit_after} frames")

        # Start the client in a background thread
        self.client.start(callback)

        try:
            # Wait until stop event is set or cancelled
            await self.stop_event.wait()
        except asyncio.CancelledError:
            self.logger.info("Service cancelled")
        finally:
            self.logger.info("Stopping client...")
            self.logger.info(f"Total frames processed: {self.frame_count}")
            self.client.stop()

    def process_frame_oneway(self, input_frame: np.ndarray) -> None:
        """
        Process a frame in one-way mode.

        Args:
            input_frame: Input frame as numpy array
        """
        self.frame_count += 1

        height, width = input_frame.shape[:2]
        self.logger.info(
            f"Processed frame {self.frame_count} ({width}x{height}) in one-way mode"
        )

        # Check if we should exit
        if self.exit_after > 0 and self.frame_count >= self.exit_after:
            self.logger.info(f"Reached {self.exit_after} frames, exiting...")
            # Schedule stop_event.set() on the main event loop to avoid deadlock
            # In duplex mode, this callback runs in server thread, not main thread
            if self._loop and self._loop.is_running():
                self._loop.call_soon_threadsafe(self.stop_event.set)
            else:
                self.stop_event.set()

    def process_frame_duplex(self, input_frame: np.ndarray, output_frame: np.ndarray) -> None:
        """
        Process frames in duplex mode.

        Args:
            input_frame: Input frame as numpy array
            output_frame: Output frame to fill with processed data
        """
        self.frame_count += 1

        # Copy input to output first
        np.copyto(output_frame, input_frame)

        # Add overlay text to the output
        cv2.putText(
            output_frame,
            "DUPLEX",
            (10, 30),
            cv2.FONT_HERSHEY_SIMPLEX,
            1.0,
            (0, 0, 255),  # Red in BGR
            2,
        )

        # Add timestamp overlay
        timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        cv2.putText(
            output_frame,
            timestamp,
            (10, 60),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.5,
            (255, 255, 255),  # White
            1,
        )

        # Add frame counter
        cv2.putText(
            output_frame,
            f"Frame: {self.frame_count}",
            (10, 90),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.5,
            (255, 255, 255),  # White
            1,
        )

        height, width = input_frame.shape[:2]
        self.logger.info(
            f"Processed frame {self.frame_count} ({width}x{height}) in duplex mode"
        )

        # Check if we should exit
        if self.exit_after > 0 and self.frame_count >= self.exit_after:
            self.logger.info(f"Reached {self.exit_after} frames, exiting...")
            # Schedule stop_event.set() on the main event loop to avoid deadlock
            # In duplex mode, this callback runs in server thread, not main thread
            if self._loop and self._loop.is_running():
                self._loop.call_soon_threadsafe(self.stop_event.set)
            else:
                self.stop_event.set()


async def main():
    """Main entry point for the application."""
    # Parse command line arguments
    parser = argparse.ArgumentParser(
        description="RocketWelder SDK SimpleClient - Python Version",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  # One-way mode (receive only)
  %(prog)s "shm://test_buffer?mode=OneWay" --exit-after 10
  
  # Duplex mode (receive and send)
  %(prog)s "shm://test_buffer?mode=Duplex" --exit-after 10
  
  # Using environment variable
  CONNECTION_STRING="shm://test_buffer?mode=OneWay" %(prog)s
        """,
    )

    parser.add_argument(
        "connection_string",
        nargs="?",
        help="Connection string (e.g., 'shm://buffer_name?mode=Duplex')",
    )
    parser.add_argument(
        "--exit-after",
        type=int,
        default=-1,
        help="Exit after processing N frames (-1 for unlimited)",
    )
    parser.add_argument(
        "--log-level",
        choices=["DEBUG", "INFO", "WARNING", "ERROR"],
        default="INFO",
        help="Logging level",
    )

    args = parser.parse_args()

    # Get connection string from argument or environment variable
    connection_string = args.connection_string or os.environ.get("CONNECTION_STRING")
    if not connection_string:
        parser.error("Connection string must be provided as argument or CONNECTION_STRING environment variable")

    # Configure logging - respect ROCKET_WELDER_LOG_LEVEL env var
    # Priority: CLI arg > ROCKET_WELDER_LOG_LEVEL > default
    log_level = args.log_level
    if log_level == "INFO":  # Default value
        env_log_level = os.environ.get('ROCKET_WELDER_LOG_LEVEL')
        if env_log_level:
            log_level = env_log_level.upper()
    
    # Set up logging
    logging.basicConfig(
        level=getattr(logging, log_level),
        format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
    )
    logger = logging.getLogger("SimpleClient")
    
    # Ensure ROCKET_WELDER_LOG_LEVEL is set for SDK initialization
    if not os.environ.get('ROCKET_WELDER_LOG_LEVEL'):
        os.environ['ROCKET_WELDER_LOG_LEVEL'] = log_level

    # Print startup information
    print("=" * 40)
    print("RocketWelder SDK SimpleClient 2025")
    print("=" * 40)
    print(f"Connection: {connection_string}")
    print(f"Exit after: {args.exit_after if args.exit_after > 0 else 'unlimited'}")
    print(f"Log level: {args.log_level}")
    print("=" * 40)
    print()

    # Create client and service
    try:
        client = RocketWelderClient(connection_string)
        service = VideoProcessingService(client, args.exit_after, logger)

        # Handle signals for graceful shutdown
        loop = asyncio.get_event_loop()
        for sig in (signal.SIGTERM, signal.SIGINT):
            loop.add_signal_handler(sig, lambda: service.stop_event.set())

        # Run the service
        await service.run()

    except Exception as e:
        logger.error(f"Error: {e}", exc_info=True)
        sys.exit(1)


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("\nShutdown requested...")
        sys.exit(0)