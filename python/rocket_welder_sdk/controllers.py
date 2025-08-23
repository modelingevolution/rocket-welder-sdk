"""
Enterprise-grade controller implementations for RocketWelder SDK.
Provides OneWay and Duplex shared memory controllers for video streaming.
"""

from __future__ import annotations

import json
import logging
import threading
from abc import ABC, abstractmethod
from typing import Any, Callable

import numpy as np
from zerobuffer import BufferConfig, Frame, Reader, Writer
from zerobuffer.duplex import DuplexChannelFactory, IImmutableDuplexServer

from .connection_string import ConnectionMode, ConnectionString, Protocol
from .gst_metadata import GstCaps, GstMetadata

# Type alias for OpenCV Mat
Mat = np.ndarray[Any, Any]


class IController(ABC):
    """Abstract base class for controllers."""

    @property
    @abstractmethod
    def is_running(self) -> bool:
        """Check if the controller is running."""
        ...

    @abstractmethod
    def get_metadata(self) -> GstMetadata | None:
        """Get the current GStreamer metadata."""
        ...

    @abstractmethod
    def start(
        self, on_frame: Callable[[Mat], None], cancellation_token: threading.Event | None = None
    ) -> None:
        """
        Start the controller with a frame callback.

        Args:
            on_frame: Callback for processing frames
            cancellation_token: Optional cancellation token
        """
        ...

    @abstractmethod
    def stop(self) -> None:
        """Stop the controller."""
        ...


class OneWayShmController(IController):
    """
    One-way shared memory controller for receiving video frames.

    This controller creates a shared memory buffer that GStreamer connects to
    as a zerosink, allowing zero-copy frame reception.
    """

    def __init__(self, connection: ConnectionString, logger: logging.Logger | None = None):
        """
        Initialize the one-way controller.

        Args:
            connection: Connection string configuration
            logger: Optional logger instance
        """
        if connection.protocol != Protocol.SHM:
            raise ValueError(
                f"OneWayShmController requires SHM protocol, got {connection.protocol}"
            )

        self._connection = connection
        self._logger = logger or logging.getLogger(__name__)
        self._reader_logger = logging.getLogger(f"{__name__}.Reader")
        self._reader: Reader | None = None
        self._gst_caps: GstCaps | None = None
        self._metadata: GstMetadata | None = None
        self._is_running = False
        self._worker_thread: threading.Thread | None = None
        self._cancellation_token: threading.Event | None = None

    @property
    def is_running(self) -> bool:
        """Check if the controller is running."""
        return self._is_running

    def get_metadata(self) -> GstMetadata | None:
        """Get the current GStreamer metadata."""
        return self._metadata

    def start(
        self, on_frame: Callable[[Mat], None], cancellation_token: threading.Event | None = None
    ) -> None:
        """
        Start receiving frames from shared memory.

        Args:
            on_frame: Callback for processing received frames
            cancellation_token: Optional cancellation token
        """
        if self._is_running:
            raise RuntimeError("Controller is already running")

        self._logger.debug(
            "Starting OneWayShmController for buffer '%s'", self._connection.buffer_name
        )
        self._is_running = True
        self._cancellation_token = cancellation_token

        # Create buffer configuration
        config = BufferConfig(
            metadata_size=int(self._connection.metadata_size),
            payload_size=int(self._connection.buffer_size),
        )

        # Create reader (we are the server, GStreamer connects to us)
        # Pass logger to Reader for better debugging
        if not self._connection.buffer_name:
            raise ValueError("Buffer name is required for shared memory connection")
        self._reader = Reader(self._connection.buffer_name, config, logger=self._reader_logger)

        self._logger.info(
            "Created shared memory buffer '%s' with size %s and metadata %s",
            self._connection.buffer_name,
            self._connection.buffer_size,
            self._connection.metadata_size,
        )

        # Start processing thread
        self._worker_thread = threading.Thread(
            target=self._process_frames,
            args=(on_frame,),
            name=f"RocketWelder-{self._connection.buffer_name}",
        )
        self._worker_thread.start()

    def stop(self) -> None:
        """Stop the controller and clean up resources."""
        if not self._is_running:
            return

        self._logger.debug("Stopping controller for buffer '%s'", self._connection.buffer_name)
        self._is_running = False

        # Wait for worker thread to finish
        if self._worker_thread and self._worker_thread.is_alive():
            timeout_ms = self._connection.timeout_ms + 50
            self._worker_thread.join(timeout=timeout_ms / 1000.0)

        # Clean up reader
        if self._reader:
            self._reader.close()
            self._reader = None

        self._worker_thread = None
        self._logger.info("Stopped controller for buffer '%s'", self._connection.buffer_name)

    def _process_frames(self, on_frame: Callable[[Mat], None]) -> None:
        """
        Process frames from shared memory.

        Args:
            on_frame: Callback for processing frames
        """
        try:
            # Process first frame to get metadata
            self._on_first_frame(on_frame)

            # Process remaining frames
            while self._is_running and (
                not self._cancellation_token or not self._cancellation_token.is_set()
            ):
                try:
                    # ReadFrame blocks until frame available
                    # Use timeout in seconds directly
                    timeout_seconds = self._connection.timeout_ms / 1000.0
                    frame = self._reader.read_frame(timeout=timeout_seconds)  # type: ignore[union-attr]

                    if frame is None or not frame.is_valid:
                        continue  # Skip invalid frames

                    # Process frame data using context manager
                    with frame:
                        # Create Mat from frame data (zero-copy when possible)
                        mat = self._create_mat_from_frame(frame)
                        if mat is not None:
                            on_frame(mat)

                except Exception as e:
                    # Log specific error types like C#
                    error_type = type(e).__name__
                    if "ReaderDead" in error_type or "WriterDead" in error_type:
                        self._logger.info(
                            "Writer disconnected from buffer '%s'", self._connection.buffer_name
                        )
                        self._is_running = False
                        break
                    elif "BufferFull" in error_type:
                        self._logger.error(
                            "Buffer full on '%s': %s", self._connection.buffer_name, e
                        )
                        if not self._is_running:
                            break
                    elif "FrameTooLarge" in error_type:
                        self._logger.error(
                            "Frame too large on '%s': %s", self._connection.buffer_name, e
                        )
                        if not self._is_running:
                            break
                    elif "ZeroBuffer" in error_type:
                        self._logger.error(
                            "ZeroBuffer error on '%s': %s", self._connection.buffer_name, e
                        )
                        if not self._is_running:
                            break
                    else:
                        self._logger.error(
                            "Unexpected error processing frame from buffer '%s': %s",
                            self._connection.buffer_name,
                            e,
                        )
                        if not self._is_running:
                            break

        except Exception as e:
            self._logger.error("Fatal error in frame processing loop: %s", e)
            self._is_running = False

    def _on_first_frame(self, on_frame: Callable[[Mat], None]) -> None:
        """
        Process the first frame and extract metadata.
        Matches C# OnFirstFrame behavior - loops until valid frame received.

        Args:
            on_frame: Callback for processing frames
        """
        while self._is_running and (
            not self._cancellation_token or not self._cancellation_token.is_set()
        ):
            try:
                # ReadFrame blocks until frame available
                timeout_seconds = self._connection.timeout_ms / 1000.0
                frame = self._reader.read_frame(timeout=timeout_seconds)  # type: ignore[union-attr]

                if frame is None or not frame.is_valid:
                    continue  # Skip invalid frames

                with frame:
                    # Read metadata - we ALWAYS expect metadata (like C#)
                    metadata_bytes = self._reader.get_metadata()  # type: ignore[union-attr]
                    if metadata_bytes:
                        try:
                            # Log raw metadata for debugging
                            self._logger.debug(
                                "Raw metadata: %d bytes, type=%s, first 100 bytes: %s",
                                len(metadata_bytes),
                                type(metadata_bytes),
                                bytes(metadata_bytes[:min(100, len(metadata_bytes))]),
                            )
                            
                            # Convert memoryview to bytes if needed
                            if isinstance(metadata_bytes, memoryview):
                                metadata_bytes = bytes(metadata_bytes)
                            
                            # Decode UTF-8
                            metadata_str = metadata_bytes.decode("utf-8")
                            
                            # Check if metadata is empty or all zeros
                            if not metadata_str or metadata_str == '\x00' * len(metadata_str):
                                self._logger.warning("Metadata is empty or all zeros, skipping")
                                continue
                            
                            # Find the start of JSON (skip any null bytes at the beginning)
                            json_start = metadata_str.find('{')
                            if json_start == -1:
                                self._logger.warning("No JSON found in metadata: %r", metadata_str[:100])
                                continue
                            
                            if json_start > 0:
                                self._logger.debug("Skipping %d bytes before JSON", json_start)
                                metadata_str = metadata_str[json_start:]
                            
                            # Find the end of JSON (handle null padding)
                            json_end = metadata_str.rfind('}')
                            if json_end != -1 and json_end < len(metadata_str) - 1:
                                metadata_str = metadata_str[:json_end + 1]
                            
                            metadata_json = json.loads(metadata_str)
                            self._metadata = GstMetadata.from_json(metadata_json)
                            self._gst_caps = self._metadata.caps
                            self._logger.info(
                                "Received metadata from buffer '%s': %s",
                                self._connection.buffer_name,
                                self._gst_caps,
                            )
                        except Exception as e:
                            self._logger.error("Failed to parse metadata: %s", e)
                            # Log the actual metadata content for debugging
                            if metadata_bytes:
                                self._logger.debug("Metadata content: %r", metadata_bytes[:200])
                            # Don't continue without metadata
                            continue

                    # Process first frame
                    mat = self._create_mat_from_frame(frame)
                    if mat is not None:
                        on_frame(mat)
                        return  # Successfully processed first frame

            except Exception as e:
                error_type = type(e).__name__
                if "ReaderDead" in error_type or "WriterDead" in error_type:
                    self._is_running = False
                    self._logger.info(
                        "Writer disconnected while waiting for first frame on buffer '%s'",
                        self._connection.buffer_name,
                    )
                    raise
                else:
                    self._logger.error(
                        "Error waiting for first frame on buffer '%s': %s",
                        self._connection.buffer_name,
                        e,
                    )
                    if not self._is_running:
                        break

    def _create_mat_from_frame(self, frame: Frame) -> Mat | None:
        """
        Create OpenCV Mat from frame data using GstCaps.
        Matches C# CreateMat behavior - creates Mat wrapping the data.

        Args:
            frame: ZeroBuffer frame

        Returns:
            OpenCV Mat or None if conversion failed
        """
        try:
            # Match C# CreateMat behavior: Create Mat wrapping the existing data
            if self._gst_caps and self._gst_caps.width and self._gst_caps.height:
                width = self._gst_caps.width
                height = self._gst_caps.height

                # Determine channels from format (like C# MapGStreamerFormatToEmgu)
                format_str = self._gst_caps.format or "RGB"
                if format_str in ["RGB", "BGR"]:
                    channels = 3
                elif format_str in ["RGBA", "BGRA", "ARGB", "ABGR"]:
                    channels = 4
                elif format_str in ["GRAY8", "GRAY16_LE", "GRAY16_BE"]:
                    channels = 1
                else:
                    channels = 3  # Default to RGB

                # Get frame data directly as numpy array (zero-copy view)
                # Frame.data is already a memoryview/buffer that can be wrapped
                data = np.frombuffer(frame.data, dtype=np.uint8)

                # Check data size matches expected
                expected_size = height * width * channels
                if len(data) != expected_size:
                    self._logger.error(
                        "Data size mismatch. Expected %d bytes for %dx%d with %d channels, got %d",
                        expected_size,
                        width,
                        height,
                        channels,
                        len(data),
                    )
                    return None

                # Reshape to image dimensions - this is zero-copy, just changes the view
                # This matches C#: new Mat(Height, Width, Depth, Channels, ptr, Width * Channels)
                if channels == 3:
                    mat = data.reshape((height, width, 3))
                elif channels == 1:
                    mat = data.reshape((height, width))
                elif channels == 4:
                    mat = data.reshape((height, width, 4))
                else:
                    self._logger.error("Unsupported channel count: %d", channels)
                    return None

                return mat  # type: ignore[no-any-return]

            # No caps available
            self._logger.error("No GstCaps available for frame conversion")
            return None

        except Exception as e:
            self._logger.error("Failed to convert frame to Mat: %s", e)
            return None

    def _infer_caps_from_frame(self, mat: Mat) -> None:
        """
        Infer GStreamer caps from OpenCV Mat.

        Args:
            mat: OpenCV Mat
        """
        if mat is None:
            return

        shape = mat.shape
        if len(shape) == 2:
            # Grayscale
            self._gst_caps = GstCaps(format="GRAY8", width=shape[1], height=shape[0])
        elif len(shape) == 3:
            # Color image
            self._gst_caps = GstCaps(format="BGR", width=shape[1], height=shape[0])

        self._logger.info("Inferred caps from frame: %s", self._gst_caps)


class DuplexShmController(IController):
    """
    Duplex shared memory controller for bidirectional video streaming.

    This controller supports both receiving frames from one buffer and
    sending processed frames to another buffer.
    """

    def __init__(self, connection: ConnectionString, logger: logging.Logger | None = None):
        """
        Initialize the duplex controller.

        Args:
            connection: Connection string configuration
            logger: Optional logger instance
        """
        if connection.protocol != Protocol.SHM:
            raise ValueError(
                f"DuplexShmController requires SHM protocol, got {connection.protocol}"
            )

        if connection.connection_mode != ConnectionMode.DUPLEX:
            raise ValueError(
                f"DuplexShmController requires DUPLEX mode, got {connection.connection_mode}"
            )

        self._connection = connection
        self._logger = logger or logging.getLogger(__name__)
        self._duplex_server: IImmutableDuplexServer | None = None
        self._gst_caps: GstCaps | None = None
        self._metadata: GstMetadata | None = None
        self._is_running = False
        self._on_frame_callback: Callable[[Mat, Mat], None] | None = None
        self._frame_count = 0

    @property
    def is_running(self) -> bool:
        """Check if the controller is running."""
        return self._is_running

    def get_metadata(self) -> GstMetadata | None:
        """Get the current GStreamer metadata."""
        return self._metadata

    def start(
        self,
        on_frame: Callable[[Mat, Mat], None],  # type: ignore[override]
        cancellation_token: threading.Event | None = None,
    ) -> None:
        """
        Start duplex frame processing.

        Args:
            on_frame: Callback that receives input frame and output frame to fill
            cancellation_token: Optional cancellation token
        """
        if self._is_running:
            raise RuntimeError("Controller is already running")

        self._is_running = True
        self._on_frame_callback = on_frame

        # Create buffer configuration
        config = BufferConfig(
            metadata_size=int(self._connection.metadata_size),
            payload_size=int(self._connection.buffer_size),
        )

        # Create duplex server using factory
        # Convert timeout from milliseconds to seconds for Python API
        if not self._connection.buffer_name:
            raise ValueError("Buffer name is required for shared memory connection")
        timeout_seconds = self._connection.timeout_ms / 1000.0
        factory = DuplexChannelFactory()
        self._duplex_server = factory.create_immutable_server(
            self._connection.buffer_name, config, timeout_seconds
        )

        self._logger.info(
            "Starting duplex server for channel '%s' with size %s and metadata %s",
            self._connection.buffer_name,
            self._connection.buffer_size,
            self._connection.metadata_size,
        )

        # Start server with frame processor callback
        if self._duplex_server:
            self._duplex_server.start(self._process_duplex_frame, self._on_metadata)

    def stop(self) -> None:
        """Stop the controller and clean up resources."""
        if not self._is_running:
            return

        self._logger.info("Stopping DuplexShmController")
        self._is_running = False

        # Stop the duplex server
        if self._duplex_server:
            self._duplex_server.stop()
            self._duplex_server = None

        self._logger.info("DuplexShmController stopped")

    def _on_metadata(self, metadata_bytes: bytes | memoryview) -> None:
        """
        Handle metadata from duplex channel.

        Args:
            metadata_bytes: Raw metadata bytes or memoryview
        """
        try:
            # Convert memoryview to bytes if needed
            if isinstance(metadata_bytes, memoryview):
                metadata_bytes = bytes(metadata_bytes)
            
            metadata_str = metadata_bytes.decode("utf-8")
            metadata_json = json.loads(metadata_str)
            self._metadata = GstMetadata.from_json(metadata_json)
            self._gst_caps = self._metadata.caps
            self._logger.info("Received metadata: %s", self._metadata)
        except Exception as e:
            self._logger.warning("Failed to parse metadata: %s", e)

    def _process_duplex_frame(self, request_frame: Frame, response_writer: Writer) -> None:
        """
        Process a frame in duplex mode.

        Args:
            request_frame: Input frame from the request
            response_writer: Writer for the response frame
        """
        try:
            if not self._on_frame_callback:
                return

            self._frame_count += 1

            # Convert input frame to Mat
            input_mat = self._frame_to_mat(request_frame)
            if input_mat is None:
                return

            # Get buffer for output frame - use context manager for RAII
            with response_writer.get_frame_buffer(request_frame.size) as output_buffer:
                # Create output Mat from buffer (zero-copy)
                if self._gst_caps:
                    height = self._gst_caps.height or 480
                    width = self._gst_caps.width or 640

                    if self._gst_caps.format == "RGB" or self._gst_caps.format == "BGR":
                        output_mat = np.frombuffer(output_buffer, dtype=np.uint8).reshape(
                            (height, width, 3)
                        )
                    elif self._gst_caps.format == "GRAY8":
                        output_mat = np.frombuffer(output_buffer, dtype=np.uint8).reshape(
                            (height, width)
                        )
                    else:
                        # Default to same shape as input
                        output_mat = np.frombuffer(output_buffer, dtype=np.uint8).reshape(
                            input_mat.shape
                        )
                else:
                    # Use same shape as input
                    output_mat = np.frombuffer(output_buffer, dtype=np.uint8).reshape(input_mat.shape)

                # Call user's processing function
                self._on_frame_callback(input_mat, output_mat)

            # Commit the response frame after buffer is released
            response_writer.commit_frame()

            self._logger.debug(
                "Processed duplex frame %d (%dx%d)",
                self._frame_count,
                input_mat.shape[1],
                input_mat.shape[0],
            )

        except Exception as e:
            self._logger.error("Error processing duplex frame: %s", e)

    def _frame_to_mat(self, frame: Frame) -> Mat | None:
        """Convert frame to OpenCV Mat (reuse from OneWayShmController)."""
        # Implementation is same as OneWayShmController
        return OneWayShmController._create_mat_from_frame(self, frame)  # type: ignore[arg-type]
