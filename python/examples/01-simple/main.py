#!/usr/bin/env python3
"""
Minimal example showing how to process video frames with RocketWelder SDK.
Adds a simple timestamp overlay to frames.
"""

import logging
import sys
import time
from datetime import datetime
from typing import Any, Callable, Union

import cv2
import numpy as np
import numpy.typing as npt

import rocket_welder_sdk as rw


def setup_logging() -> logging.Logger:
    """Setup logging with console and file handlers."""
    # Create main logger
    logger = logging.getLogger(__name__)
    logger.setLevel(logging.DEBUG)

    # Clear any existing handlers
    logger.handlers.clear()

    # Create formatters with more detail
    detailed_formatter = logging.Formatter(
        "%(asctime)s.%(msecs)03d - %(name)s - %(levelname)s - [%(filename)s:%(lineno)d] - %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S",
    )

    simple_formatter = logging.Formatter(
        "%(asctime)s - %(name)s - %(levelname)s - %(message)s", datefmt="%Y-%m-%d %H:%M:%S"
    )

    # Console handler - show DEBUG level for controllers
    console_handler = logging.StreamHandler(sys.stdout)
    console_handler.setLevel(logging.DEBUG)  # Changed from INFO to DEBUG
    console_handler.setFormatter(simple_formatter)
    logger.addHandler(console_handler)

    # File handler with detailed format
    file_handler = logging.FileHandler("/tmp/python.log")
    file_handler.setLevel(logging.DEBUG)
    file_handler.setFormatter(detailed_formatter)
    logger.addHandler(file_handler)

    # Configure logging for zerobuffer-ipc with DEBUG level
    zerobuffer_logger = logging.getLogger("zerobuffer")
    zerobuffer_logger.setLevel(logging.DEBUG)
    zerobuffer_logger.handlers.clear()  # Clear existing handlers
    zerobuffer_logger.addHandler(console_handler)
    zerobuffer_logger.addHandler(file_handler)
    zerobuffer_logger.propagate = False  # Prevent propagation to root

    # Configure logging for rocket-welder-sdk with DEBUG level
    rw_logger = logging.getLogger("rocket_welder_sdk")
    rw_logger.setLevel(logging.DEBUG)
    rw_logger.handlers.clear()  # Clear existing handlers
    rw_logger.addHandler(console_handler)
    rw_logger.addHandler(file_handler)
    rw_logger.propagate = False  # Prevent propagation to root

    # Specifically configure controllers module with DEBUG
    # No need to add handlers here since it will inherit from rocket_welder_sdk
    controllers_logger = logging.getLogger("rocket_welder_sdk.controllers")
    controllers_logger.setLevel(logging.DEBUG)
    # Let it propagate to parent (rocket_welder_sdk) which has the handlers

    return logger


# Global logger instance
logger: logging.Logger = None  # type: ignore


def log(message: str, level: int = logging.INFO) -> None:
    """Log a message to both console and file."""
    if logger:
        logger.log(level, message)


def main() -> None:
    """Main entry point."""
    # Initialize logging as the first thing
    global logger
    logger = setup_logging()

    log("Starting")
    # Create client - automatically detects connection from args or env
    client = rw.Client.from_(sys.argv)

    log(f"Connected: {client.connection}")

    # Process frames based on mode
    callback: Union[
        Callable[[npt.NDArray[Any]], None],
        Callable[[npt.NDArray[Any], npt.NDArray[Any]], None],
    ]

    if client.connection.connection_mode == rw.ConnectionMode.DUPLEX:
        # Duplex mode: receive input, write to output
        def process_frame_duplex(
            input_frame: npt.NDArray[Any], output_frame: npt.NDArray[Any]
        ) -> None:
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

        callback = process_frame_duplex
    else:
        # OneWay mode: modify frame in-place
        def process_frame_oneway(frame: npt.NDArray[Any]) -> None:
            timestamp = datetime.now().strftime("%H:%M:%S")
            cv2.putText(
                frame, timestamp, (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2
            )

        callback = process_frame_oneway

    # Start processing
    client.start(callback)

    # Check if preview is enabled and handle display
    try:
        if client.connection.parameters.get("preview", "false").lower() == "true":
            # Show preview - blocks until 'q' pressed or stopped
            log("Showing preview... Press 'q' to stop")
            client.show()
        else:
            # No preview, just keep running
            while client.is_running:
                time.sleep(0.1)
    except KeyboardInterrupt:
        log("Stopping...", logging.INFO)
    finally:
        client.stop()


if __name__ == "__main__":
    main()
