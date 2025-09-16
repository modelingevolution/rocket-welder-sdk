#!/usr/bin/env python3
"""
Minimal example showing how to process video frames with RocketWelder SDK.
Adds a simple timestamp overlay to frames.
"""

import sys
import time
from datetime import datetime
from typing import Any, Callable, Union

import cv2
import numpy as np
import numpy.typing as npt

import rocket_welder_sdk as rw


def main() -> None:
    """Main entry point."""
    # Create client - automatically detects connection from args or env
    client = rw.Client.from_(sys.argv)

    print(f"Connected: {client.connection}")

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

    # Keep running until interrupted
    try:
        while client.is_running:
            time.sleep(0.1)
    except KeyboardInterrupt:
        print("\nStopping...")
    finally:
        client.stop()


if __name__ == "__main__":
    main()
