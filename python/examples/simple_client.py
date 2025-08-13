#!/usr/bin/env python3
"""
Simple client example for RocketWelder SDK

Mirrors the C# SimpleClient implementation.
"""

import sys
import os
import logging
import argparse
import signal
import threading
from datetime import datetime
from pathlib import Path

# Add SDK to path
sdk_path = Path(__file__).parent.parent / "src"
if sdk_path.exists():
    sys.path.insert(0, str(sdk_path))

import cv2
import numpy as np
from rocket_welder_sdk import RocketWelderClient, ConnectionString


class VideoProcessingService:
    """Video processing service that mirrors C# BackgroundService"""
    
    def __init__(self, client: RocketWelderClient, exit_after: int = -1):
        """
        Initialize video processing service
        
        Args:
            client: RocketWelderClient instance
            exit_after: Number of frames to process before exiting (-1 for infinite)
        """
        self.client = client
        self.exit_after = exit_after
        self.frame_count = 0
        self.logger = logging.getLogger(__name__)
        self.stop_event = threading.Event()
        
    def process_frame(self, frame: np.ndarray) -> None:
        """
        Process a single frame (zero-copy)
        
        Args:
            frame: NumPy array view of frame data (no copy made)
        """
        self.frame_count += 1
        
        # Add overlay text using OpenCV
        # Note: This modifies the shared memory directly (zero-copy)
        cv2.putText(frame, "Processing", (10, 30),
                   cv2.FONT_HERSHEY_SIMPLEX, 1.0, (0, 255, 0), 2)
        
        # Add timestamp overlay
        timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        cv2.putText(frame, timestamp, (10, 60),
                   cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
        
        # Add frame counter
        cv2.putText(frame, f"Frame: {self.frame_count}", (10, 90),
                   cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
        
        self.logger.info(
            "Processed frame %d (%dx%d)",
            self.frame_count, frame.shape[1], frame.shape[0]
        )
        
        # Check if we should exit
        if self.exit_after > 0 and self.frame_count >= self.exit_after:
            self.logger.info("Reached %d frames, exiting...", self.exit_after)
            self.stop_event.set()
    
    def run(self) -> None:
        """Run the video processing service"""
        self.logger.info("Starting RocketWelder client...")
        self.logger.info("Connection: %s", self.client.connection)
        
        # Suggest GStreamer test command
        buffer_name = self.client.connection.buffer_name or "default"
        num_buffers = self.exit_after if self.exit_after > 0 else 100
        self.logger.info(
            "Can be tested with:\n\n\t"
            "gst-launch-1.0 videotestsrc num-buffers=%d pattern=ball ! "
            "video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! "
            "zerosink buffer-name=%s sync=false\n",
            num_buffers, buffer_name
        )
        
        if self.exit_after > 0:
            self.logger.info("Will exit after %d frames", self.exit_after)
        
        # Set up frame processing callback
        self.client.on_frame(self.process_frame)
        
        # Start processing
        self.client.start()
        
        # Wait until stopped
        try:
            self.stop_event.wait()
        except KeyboardInterrupt:
            self.logger.info("Interrupted by user")
        
        self.logger.info("Stopping client...")
        self.logger.info("Total frames processed: %d", self.frame_count)
        self.client.stop()


def main():
    """Main entry point"""
    # Parse arguments
    parser = argparse.ArgumentParser(description="RocketWelder SDK SimpleClient")
    parser.add_argument(
        "connection_string",
        nargs="?",
        help="Connection string (e.g., shm://mybuffer)"
    )
    parser.add_argument(
        "--exit-after",
        type=int,
        default=-1,
        help="Exit after N frames (-1 for infinite)"
    )
    parser.add_argument(
        "--verbose",
        action="store_true",
        help="Enable verbose logging"
    )
    
    args = parser.parse_args()
    
    # Configure logging
    log_level = logging.DEBUG if args.verbose else logging.INFO
    logging.basicConfig(
        level=log_level,
        format="%(asctime)s [%(levelname)s] %(name)s: %(message)s"
    )
    
    # Print arguments for debugging
    print("========================================")
    print("RocketWelder SDK SimpleClient")
    print("========================================")
    print(f"Arguments received: {len(sys.argv) - 1}")
    for i, arg in enumerate(sys.argv[1:]):
        print(f"  [{i}]: {arg}")
    print("========================================")
    print()
    
    # Get connection string from args or environment
    if args.connection_string:
        os.environ["CONNECTION_STRING"] = args.connection_string
    
    # Create client from environment (will use CONNECTION_STRING)
    client = RocketWelderClient.from_environment()
    
    # Create and run service
    service = VideoProcessingService(client, args.exit_after)
    
    # Handle signals for graceful shutdown
    def signal_handler(signum, frame):
        print("\nReceived signal, shutting down...")
        service.stop_event.set()
    
    signal.signal(signal.SIGINT, signal_handler)
    signal.signal(signal.SIGTERM, signal_handler)
    
    # Run the service
    service.run()


if __name__ == "__main__":
    main()