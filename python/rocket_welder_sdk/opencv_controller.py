"""
OpenCV-based controller for video file playback and network streams.
Provides support for file:// and mjpeg:// protocols.
"""

from __future__ import annotations

import logging
import os
import threading
import time
from pathlib import Path
from typing import TYPE_CHECKING, Any, Callable

import cv2
import numpy as np
import numpy.typing as npt

from .connection_string import ConnectionMode, ConnectionString, Protocol
from .controllers import IController
from .gst_metadata import GstCaps, GstMetadata
from .periodic_timer import PeriodicTimerSync

if TYPE_CHECKING:
    Mat = npt.NDArray[np.uint8]
else:
    Mat = np.ndarray  # type: ignore[misc]

logger = logging.getLogger(__name__)


class OpenCvController(IController):
    """
    Controller for video sources using OpenCV VideoCapture.

    Supports:
    - File playback with optional looping
    - MJPEG network streams over HTTP/TCP
    """

    def __init__(self, connection: ConnectionString) -> None:
        """
        Initialize the OpenCV controller.

        Args:
            connection: Connection string configuration
        """
        if not (connection.protocol == Protocol.FILE or bool(connection.protocol & Protocol.MJPEG)):  # type: ignore[operator]
            raise ValueError(
                f"OpenCvController requires FILE or MJPEG protocol, got {connection.protocol}"
            )

        self._connection = connection
        self._capture: cv2.VideoCapture | None = None
        self._metadata: GstMetadata | None = None
        self._is_running = False
        self._worker_thread: threading.Thread | None = None
        self._cancellation_token: threading.Event | None = None

        # Parse parameters for file protocol
        self._loop = (
            connection.protocol == Protocol.FILE
            and connection.parameters.get("loop", "false").lower() == "true"
        )

        # Note: Preview is now handled at the client level via show() method
        # This avoids X11/WSL threading issues with OpenCV GUI functions

    @property
    def is_running(self) -> bool:
        """Check if the controller is running."""
        return self._is_running

    def get_metadata(self) -> GstMetadata | None:
        """Get the current video metadata."""
        return self._metadata

    def start(
        self,
        on_frame: (
            Callable[[npt.NDArray[Any]], None]
            | Callable[[npt.NDArray[Any], npt.NDArray[Any]], None]
        ),
        cancellation_token: threading.Event | None = None,
    ) -> None:
        """
        Start processing video frames.

        Args:
            on_frame: Callback for frame processing
            cancellation_token: Optional cancellation token
        """
        if self._is_running:
            raise RuntimeError("Controller is already running")

        self._is_running = True
        self._cancellation_token = cancellation_token

        # Get video source
        source = self._get_source()
        logger.info("Opening video source: %s (loop=%s)", source, self._loop)

        # Create VideoCapture
        self._capture = cv2.VideoCapture(source)

        if not self._capture.isOpened():
            self._capture.release()
            self._capture = None
            self._is_running = False
            raise RuntimeError(f"Failed to open video source: {source}")

        # Get video properties
        width = int(self._capture.get(cv2.CAP_PROP_FRAME_WIDTH))
        height = int(self._capture.get(cv2.CAP_PROP_FRAME_HEIGHT))
        fps = self._capture.get(cv2.CAP_PROP_FPS)
        frame_count = int(self._capture.get(cv2.CAP_PROP_FRAME_COUNT))

        # Create metadata
        caps = GstCaps.from_simple(width, height, "RGB")
        self._metadata = GstMetadata(
            type="video",
            version="1.0",
            caps=caps,
            element_name=(
                "file-capture" if self._connection.protocol == Protocol.FILE else "opencv-capture"
            ),
        )

        logger.info(
            "Video source opened: %dx%d @ %.1ffps, %d frames", width, height, fps, frame_count
        )

        # Determine callback type and start worker thread
        if self._connection.connection_mode == ConnectionMode.DUPLEX:
            # For duplex mode with file/mjpeg, we allocate output but process as one-way
            def duplex_wrapper(frame: npt.NDArray[Any]) -> None:
                output = np.empty_like(frame)
                on_frame(frame, output)  # type: ignore[call-arg]

            self._worker_thread = threading.Thread(
                target=self._process_frames,
                args=(duplex_wrapper, fps),
                name=f"RocketWelder-OpenCV-{Path(source).stem}",
            )
        else:
            self._worker_thread = threading.Thread(
                target=self._process_frames,
                args=(on_frame, fps),
                name=f"RocketWelder-OpenCV-{Path(source).stem}",
            )

        self._worker_thread.start()

    def stop(self) -> None:
        """Stop the controller and clean up resources."""
        if not self._is_running:
            return

        logger.debug("Stopping OpenCV controller")
        self._is_running = False

        # Wait for worker thread
        if self._worker_thread and self._worker_thread.is_alive():
            timeout_s = (self._connection.timeout_ms + 50) / 1000.0
            self._worker_thread.join(timeout=timeout_s)

        # Clean up capture
        if self._capture:
            self._capture.release()
            self._capture = None

        self._worker_thread = None
        logger.info("Stopped OpenCV controller")

    def _get_source(self) -> str:
        """
        Get the video source string for OpenCV.

        Returns:
            Source string for VideoCapture

        Raises:
            FileNotFoundError: If file doesn't exist
            ValueError: If file path is missing
        """
        if self._connection.protocol == Protocol.FILE:
            if not self._connection.file_path:
                raise ValueError("File path is required for file protocol")

            if not os.path.exists(self._connection.file_path):
                raise FileNotFoundError(f"Video file not found: {self._connection.file_path}")

            return self._connection.file_path

        elif bool(self._connection.protocol & Protocol.MJPEG):  # type: ignore[operator]
            # Construct URL from host:port (no path support yet)
            if bool(self._connection.protocol & Protocol.HTTP):  # type: ignore[operator]
                return f"http://{self._connection.host}:{self._connection.port}"
            elif bool(self._connection.protocol & Protocol.TCP):  # type: ignore[operator]
                return f"tcp://{self._connection.host}:{self._connection.port}"
            else:
                return f"http://{self._connection.host}:{self._connection.port}"

        else:
            raise ValueError(f"Unsupported protocol: {self._connection.protocol}")

    def _process_frames(self, on_frame: Callable[[npt.NDArray[Any]], None], fps: float) -> None:
        """
        Process video frames in a loop.

        Args:
            on_frame: Callback for each frame
            fps: Frames per second for timing
        """
        if not self._capture:
            return

        # Use PeriodicTimer for precise frame timing (especially important for file playback)
        timer = None
        if self._connection.protocol == Protocol.FILE and fps > 0:
            # Create timer for file playback at specified FPS
            timer = PeriodicTimerSync(1.0 / fps)
            logger.debug("Using PeriodicTimer for file playback at %.1f FPS", fps)

        try:
            while self._is_running:
                if self._cancellation_token and self._cancellation_token.is_set():
                    break

                try:
                    # Read frame
                    ret, frame = self._capture.read()

                    if not ret:
                        if self._connection.protocol == Protocol.FILE and self._loop:
                            # Loop: Reset to beginning
                            self._capture.set(cv2.CAP_PROP_POS_FRAMES, 0)
                            logger.debug("Looping video from beginning")
                            continue
                        elif self._connection.protocol == Protocol.FILE:
                            # File ended without loop
                            logger.info("Video file ended")
                            break
                        else:
                            # Network stream issue
                            logger.warning("Failed to read frame from stream")
                            time.sleep(0.01)
                            continue

                    if hasattr(frame, "size") and frame.size == 0:
                        time.sleep(0.01)
                        continue

                    # Process frame
                    on_frame(frame)

                    # Control frame rate for file playback using PeriodicTimer
                    if timer:
                        # Wait for next tick - this provides precise timing
                        if not timer.wait_for_next_tick():
                            # Timer disposed or timed out
                            break
                    elif self._connection.protocol != Protocol.FILE:
                        # For network streams, we process as fast as they arrive
                        # No artificial delay needed
                        pass

                except Exception as e:
                    logger.error("Error processing frame: %s", e)
                    if not self._is_running:
                        break
                    time.sleep(0.1)

        finally:
            if timer:
                timer.dispose()

        self._is_running = False
