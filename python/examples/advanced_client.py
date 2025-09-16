#!/usr/bin/env python3
"""
Advanced RocketWelder client with FPS display, overlays, and UI controls.
"""

import asyncio
import os
import sys
import time
import uuid
from datetime import datetime
from typing import Any, Optional

import cv2
import numpy as np
import numpy.typing as npt

import rocket_welder_sdk as rw
from rocket_welder_sdk.ui import ArrowDirection, RegionName, UiService


class VideoProcessor:
    """Processes video frames with overlays and optional UI controls."""

    def __init__(self, session_id: Optional[str] = None) -> None:
        """Initialize video processor."""
        self.frame_count: int = 0
        self.fps: float = 0.0
        self.last_time: float = time.time()
        self.crosshair: npt.NDArray[np.float32] = np.array([320.0, 240.0], dtype=np.float32)
        self.velocity: npt.NDArray[np.float32] = np.array([0.0, 0.0], dtype=np.float32)
        self.session_id: Optional[str] = session_id
        self.ui_service: Optional[UiService] = None
        self.arrow_grid: Optional[Any] = None  # ArrowGridControl type

    async def setup_ui(self) -> None:
        """Initialize UI controls if session ID is available."""
        if not self.session_id:
            return

        try:
            # Create UI service
            self.ui_service = UiService.from_session_id(uuid.UUID(self.session_id))

            # Initialize EventStore if configured
            eventstore = os.environ.get("EventStore")
            if eventstore:
                from py_micro_plumberd import EventStoreClient

                client = EventStoreClient(eventstore)
                await self.ui_service.initialize(client)

            # Create arrow control
            self.arrow_grid = self.ui_service.factory.define_arrow_grid("nav")
            self.arrow_grid.on_arrow_down = self.on_arrow_down
            self.arrow_grid.on_arrow_up = self.on_arrow_up
            self.ui_service[RegionName.PREVIEW_BOTTOM_CENTER].add(self.arrow_grid)

            await self.ui_service.do()
            print("UI controls initialized")
        except Exception as e:
            print(f"UI setup failed: {e}")

    def on_arrow_down(self, sender: Any, direction: ArrowDirection) -> None:
        """Handle arrow key press."""
        speed = 5.0
        if direction == ArrowDirection.UP:
            self.velocity[1] = -speed
        elif direction == ArrowDirection.DOWN:
            self.velocity[1] = speed
        elif direction == ArrowDirection.LEFT:
            self.velocity[0] = -speed
        elif direction == ArrowDirection.RIGHT:
            self.velocity[0] = speed

    def on_arrow_up(self, sender: Any, direction: ArrowDirection) -> None:
        """Stop movement on arrow release."""
        self.velocity[:] = 0.0

    def process_duplex(self, input_frame: npt.NDArray[Any], output_frame: npt.NDArray[Any]) -> None:
        """Process frame in duplex mode."""
        self.frame_count += 1

        # Update FPS
        current_time = time.time()
        if current_time - self.last_time > 0:
            self.fps = 1.0 / (current_time - self.last_time)
            self.last_time = current_time

        # Update crosshair position
        h, w = input_frame.shape[:2]
        if self.frame_count == 1:
            self.crosshair = np.array([w / 2, h / 2], dtype=np.float32)
        self.crosshair += self.velocity
        self.crosshair[0] = np.clip(self.crosshair[0], 20, w - 20)
        self.crosshair[1] = np.clip(self.crosshair[1], 20, h - 20)

        # Copy and add overlays
        np.copyto(output_frame, input_frame)

        # Add text overlays
        cv2.putText(output_frame, "DUPLEX", (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1.0, (0, 0, 255), 2)
        cv2.putText(
            output_frame,
            datetime.now().strftime("%H:%M:%S"),
            (10, 60),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.7,
            (255, 255, 255),
            1,
        )
        cv2.putText(
            output_frame,
            f"Frame: {self.frame_count}",
            (10, 90),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.5,
            (255, 255, 255),
            1,
        )
        cv2.putText(
            output_frame,
            f"FPS: {self.fps:.1f}",
            (10, 120),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.5,
            (0, 255, 0),
            1,
        )

        # Draw crosshair
        x, y = int(self.crosshair[0]), int(self.crosshair[1])
        cv2.line(output_frame, (x - 20, y), (x + 20, y), (0, 255, 255), 2)
        cv2.line(output_frame, (x, y - 20), (x, y + 20), (0, 255, 255), 2)
        cv2.circle(output_frame, (x, y), 3, (0, 0, 255), -1)

    def process_oneway(self, frame: npt.NDArray[Any]) -> None:
        """Process frame in one-way mode."""
        self.frame_count += 1
        cv2.putText(
            frame,
            f"Frame {self.frame_count}",
            (10, 30),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.7,
            (255, 255, 255),
            2,
        )


async def main() -> None:
    """Main entry point."""
    # Get configuration from environment
    session_id = os.environ.get("SessionId")

    # Create client
    client = rw.Client.from_(sys.argv)
    print(f"Connected: {client.connection}")

    # Create processor
    processor = VideoProcessor(session_id)
    await processor.setup_ui()

    # Choose processing function based on mode
    from typing import Callable, Union

    callback: Union[
        Callable[[npt.NDArray[Any]], None],
        Callable[[npt.NDArray[Any], npt.NDArray[Any]], None],
    ]
    if client.connection.connection_mode == rw.ConnectionMode.DUPLEX:
        callback = processor.process_duplex
    else:
        callback = processor.process_oneway

    # Start processing in background
    client.start(callback)

    # Keep UI updated if available
    try:
        while client.is_running:
            if processor.ui_service:
                await processor.ui_service.do()
            await asyncio.sleep(0.5)
    except KeyboardInterrupt:
        print("\nShutting down...")
    finally:
        if processor.arrow_grid:
            processor.arrow_grid.dispose()
        if processor.ui_service:
            await processor.ui_service.dispose()
        client.stop()


if __name__ == "__main__":
    asyncio.run(main())
