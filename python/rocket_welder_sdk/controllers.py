"""
Enterprise-grade controller implementations for RocketWelder SDK.
Provides OneWay and Duplex shared memory controllers for video streaming.
"""

from __future__ import annotations

import json
import logging
import threading
from abc import ABC, abstractmethod
from typing import TYPE_CHECKING, Callable, Optional

import numpy as np
from zerobuffer import BufferConfig, Frame, Reader, Writer
from zerobuffer.duplex import DuplexChannelFactory
from zerobuffer.exceptions import WriterDeadException

from .connection_string import ConnectionMode, ConnectionString, Protocol
from .frame_metadata import FRAME_METADATA_SIZE, FrameMetadata
from .gst_metadata import GstCaps, GstMetadata

if TYPE_CHECKING:
    import numpy.typing as npt
    from zerobuffer.duplex import IImmutableDuplexServer

    Mat = npt.NDArray[np.uint8]
else:
    from zerobuffer.duplex import IImmutableDuplexServer

    Mat = np.ndarray  # type: ignore[misc]

# Module logger
logger = logging.getLogger(__name__)


class IController(ABC):
    """Abstract base class for controllers."""

    @property
    @abstractmethod
    def is_running(self) -> bool:
        """Check if the controller is running."""
        ...

    @abstractmethod
    def get_metadata(self) -> Optional[GstMetadata]:
        """Get the current GStreamer metadata."""
        ...

    @abstractmethod
    def start(
        self,
        on_frame: Callable[[Mat], None],  # type: ignore[valid-type]
        cancellation_token: Optional[threading.Event] = None,
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

    def __init__(self, connection: ConnectionString):
        """
        Initialize the one-way controller.

        Args:
            connection: Connection string configuration
        """
        if connection.protocol != Protocol.SHM:
            raise ValueError(
                f"OneWayShmController requires SHM protocol, got {connection.protocol}"
            )

        self._connection = connection
        self._reader: Optional[Reader] = None
        self._gst_caps: Optional[GstCaps] = None
        self._metadata: Optional[GstMetadata] = None
        self._is_running = False
        self._worker_thread: Optional[threading.Thread] = None
        self._cancellation_token: Optional[threading.Event] = None

    @property
    def is_running(self) -> bool:
        """Check if the controller is running."""
        return self._is_running

    def get_metadata(self) -> Optional[GstMetadata]:
        """Get the current GStreamer metadata."""
        return self._metadata

    def start(
        self,
        on_frame: Callable[[Mat], None],  # type: ignore[valid-type]
        cancellation_token: Optional[threading.Event] = None,
    ) -> None:
        """
        Start receiving frames from shared memory.

        Args:
            on_frame: Callback for processing received frames
            cancellation_token: Optional cancellation token
        """
        if self._is_running:
            raise RuntimeError("Controller is already running")

        logger.debug("Starting OneWayShmController for buffer '%s'", self._connection.buffer_name)
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
        self._reader = Reader(self._connection.buffer_name, config)

        logger.info(
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

        logger.debug("Stopping controller for buffer '%s'", self._connection.buffer_name)
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
        logger.info("Stopped controller for buffer '%s'", self._connection.buffer_name)

    def _process_frames(self, on_frame: Callable[[Mat], None]) -> None:  # type: ignore[valid-type]
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

                except WriterDeadException:
                    # Writer has disconnected gracefully
                    logger.info(
                        "Writer disconnected gracefully from buffer '%s'",
                        self._connection.buffer_name,
                    )
                    self._is_running = False
                    break
                except Exception as e:
                    # Log specific error types like C#
                    error_type = type(e).__name__
                    if "ReaderDead" in error_type:
                        logger.info(
                            "Reader disconnected from buffer '%s'", self._connection.buffer_name
                        )
                        self._is_running = False
                        break
                    elif "BufferFull" in error_type:
                        logger.error("Buffer full on '%s': %s", self._connection.buffer_name, e)
                        if not self._is_running:
                            break
                    elif "FrameTooLarge" in error_type:
                        logger.error("Frame too large on '%s': %s", self._connection.buffer_name, e)
                        if not self._is_running:
                            break
                    elif "ZeroBuffer" in error_type:
                        logger.error(
                            "ZeroBuffer error on '%s': %s", self._connection.buffer_name, e
                        )
                        if not self._is_running:
                            break
                    else:
                        logger.error(
                            "Unexpected error processing frame from buffer '%s': %s",
                            self._connection.buffer_name,
                            e,
                        )
                        if not self._is_running:
                            break

        except Exception as e:
            logger.error("Fatal error in frame processing loop: %s", e)
            self._is_running = False

    def _on_first_frame(self, on_frame: Callable[[Mat], None]) -> None:  # type: ignore[valid-type]
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
                            logger.debug(
                                "Raw metadata: %d bytes, type=%s, first 100 bytes: %s",
                                len(metadata_bytes),
                                type(metadata_bytes),
                                bytes(metadata_bytes[: min(100, len(metadata_bytes))]),
                            )

                            # Use helper method to parse metadata
                            metadata = self._parse_metadata_json(metadata_bytes)
                            if not metadata:
                                logger.warning("Failed to parse metadata, skipping")
                                continue

                            self._metadata = metadata
                            self._gst_caps = metadata.caps
                            logger.info(
                                "Received metadata from buffer '%s': %s",
                                self._connection.buffer_name,
                                self._gst_caps,
                            )
                        except Exception as e:
                            logger.error("Failed to parse metadata: %s", e)
                            # Log the actual metadata content for debugging
                            if metadata_bytes:
                                logger.debug("Metadata content: %r", metadata_bytes[:200])
                            # Don't continue without metadata
                            continue

                    # Process first frame
                    mat = self._create_mat_from_frame(frame)
                    if mat is not None:
                        on_frame(mat)
                        return  # Successfully processed first frame

            except WriterDeadException:
                self._is_running = False
                logger.info(
                    "Writer disconnected gracefully while waiting for first frame on buffer '%s'",
                    self._connection.buffer_name,
                )
                raise
            except Exception as e:
                error_type = type(e).__name__
                if "ReaderDead" in error_type:
                    self._is_running = False
                    logger.info(
                        "Reader disconnected while waiting for first frame on buffer '%s'",
                        self._connection.buffer_name,
                    )
                    raise
                else:
                    logger.error(
                        "Error waiting for first frame on buffer '%s': %s",
                        self._connection.buffer_name,
                        e,
                    )
                    if not self._is_running:
                        break

    def _create_mat_from_frame(self, frame: Frame) -> Optional[Mat]:  # type: ignore[valid-type]
        """
        Create OpenCV Mat from frame data using GstCaps.
        Matches C# CreateMat behavior - creates Mat wrapping the data.

        Frame data layout from GStreamer zerosink:
        [FrameMetadata (16 bytes)][Pixel Data (WxHxC bytes)]

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

                # Frame data has 16-byte FrameMetadata prefix that must be stripped
                # Layout: [FrameMetadata (16 bytes)][Pixel Data]
                if frame.size < FRAME_METADATA_SIZE:
                    logger.error(
                        "Frame too small for FrameMetadata: %d bytes (need at least %d)",
                        frame.size,
                        FRAME_METADATA_SIZE,
                    )
                    return None

                # Get pixel data (skip 16-byte FrameMetadata prefix)
                pixel_data = np.frombuffer(frame.data[FRAME_METADATA_SIZE:], dtype=np.uint8)

                # Check pixel data size matches expected
                expected_size = height * width * channels
                if len(pixel_data) != expected_size:
                    logger.error(
                        "Pixel data size mismatch. Expected %d bytes for %dx%d with %d channels, got %d",
                        expected_size,
                        width,
                        height,
                        channels,
                        len(pixel_data),
                    )
                    return None

                # Reshape to image dimensions - this is zero-copy, just changes the view
                # This matches C#: new Mat(Height, Width, Depth, Channels, ptr, Width * Channels)
                if channels == 3:
                    mat = pixel_data.reshape((height, width, 3))
                elif channels == 1:
                    mat = pixel_data.reshape((height, width))
                elif channels == 4:
                    mat = pixel_data.reshape((height, width, 4))
                else:
                    logger.error("Unsupported channel count: %d", channels)
                    return None

                return mat  # type: ignore[no-any-return]

            # No caps available - try to infer from frame size
            logger.warning("No GstCaps available, attempting to infer from frame size")

            # Frame data has 16-byte FrameMetadata prefix
            if frame.size < FRAME_METADATA_SIZE:
                logger.error(
                    "Frame too small for FrameMetadata: %d bytes (need at least %d)",
                    frame.size,
                    FRAME_METADATA_SIZE,
                )
                return None

            # Calculate pixel data size (frame size minus 16-byte metadata prefix)
            pixel_data_size = frame.size - FRAME_METADATA_SIZE

            # First, check if it's a perfect square (square frame)
            import math

            sqrt_size = math.sqrt(pixel_data_size)
            if sqrt_size == int(sqrt_size):
                # Perfect square - assume square grayscale image
                dimension = int(sqrt_size)
                logger.info(
                    f"Pixel data size {pixel_data_size} is a perfect square, "
                    f"assuming {dimension}x{dimension} grayscale"
                )
                pixel_data = np.frombuffer(frame.data[FRAME_METADATA_SIZE:], dtype=np.uint8)
                return pixel_data.reshape((dimension, dimension))  # type: ignore[no-any-return]

            # Also check for square RGB (size = width * height * 3)
            if pixel_data_size % 3 == 0:
                pixels = pixel_data_size // 3
                sqrt_pixels = math.sqrt(pixels)
                if sqrt_pixels == int(sqrt_pixels):
                    dimension = int(sqrt_pixels)
                    logger.info(
                        f"Pixel data size {pixel_data_size} suggests {dimension}x{dimension} RGB"
                    )
                    pixel_data = np.frombuffer(frame.data[FRAME_METADATA_SIZE:], dtype=np.uint8)
                    return pixel_data.reshape((dimension, dimension, 3))  # type: ignore[no-any-return]

            # Check for square RGBA (size = width * height * 4)
            if pixel_data_size % 4 == 0:
                pixels = pixel_data_size // 4
                sqrt_pixels = math.sqrt(pixels)
                if sqrt_pixels == int(sqrt_pixels):
                    dimension = int(sqrt_pixels)
                    logger.info(
                        f"Pixel data size {pixel_data_size} suggests {dimension}x{dimension} RGBA"
                    )
                    pixel_data = np.frombuffer(frame.data[FRAME_METADATA_SIZE:], dtype=np.uint8)
                    return pixel_data.reshape((dimension, dimension, 4))  # type: ignore[no-any-return]

            common_resolutions = [
                (640, 480, 3),  # VGA RGB
                (640, 480, 4),  # VGA RGBA
                (1280, 720, 3),  # 720p RGB
                (1920, 1080, 3),  # 1080p RGB
                (640, 480, 1),  # VGA Grayscale
            ]

            for width, height, channels in common_resolutions:
                if pixel_data_size == width * height * channels:
                    logger.info(f"Inferred resolution: {width}x{height} with {channels} channels")

                    # Create caps for future use
                    format_str = "RGB" if channels == 3 else "RGBA" if channels == 4 else "GRAY8"
                    self._gst_caps = GstCaps.from_simple(
                        width=width, height=height, format=format_str
                    )

                    # Create Mat from pixel data (skip 16-byte FrameMetadata prefix)
                    pixel_data = np.frombuffer(frame.data[FRAME_METADATA_SIZE:], dtype=np.uint8)
                    if channels == 3:
                        return pixel_data.reshape((height, width, 3))  # type: ignore[no-any-return]
                    elif channels == 1:
                        return pixel_data.reshape((height, width))  # type: ignore[no-any-return]
                    elif channels == 4:
                        return pixel_data.reshape((height, width, 4))  # type: ignore[no-any-return]

            logger.error(f"Could not infer resolution for pixel data size {pixel_data_size}")
            return None

        except Exception as e:
            logger.error("Failed to convert frame to Mat: %s", e)
            return None

    def _parse_metadata_json(self, metadata_bytes: bytes | memoryview) -> GstMetadata | None:
        """
        Parse metadata JSON from bytes, handling null padding and boundaries.

        Args:
            metadata_bytes: Raw metadata bytes or memoryview

        Returns:
            GstMetadata object or None if parsing fails
        """
        try:
            # Convert to string
            if isinstance(metadata_bytes, memoryview):
                metadata_bytes = bytes(metadata_bytes)
            metadata_str = metadata_bytes.decode("utf-8")

            # Find JSON boundaries (handle null padding)
            json_start = metadata_str.find("{")
            if json_start < 0:
                logger.debug("No JSON found in metadata")
                return None

            json_end = metadata_str.rfind("}")
            if json_end <= json_start:
                logger.debug("Invalid JSON boundaries in metadata")
                return None

            # Extract JSON
            metadata_str = metadata_str[json_start : json_end + 1]

            # Parse JSON
            metadata_json = json.loads(metadata_str)
            metadata = GstMetadata.from_json(metadata_json)
            return metadata

        except Exception as e:
            logger.debug("Failed to parse metadata JSON: %s", e)
            return None

    def _infer_caps_from_frame(self, mat: Mat) -> None:  # type: ignore[valid-type]
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
            self._gst_caps = GstCaps.from_simple(width=shape[1], height=shape[0], format="GRAY8")
        elif len(shape) == 3:
            # Color image
            self._gst_caps = GstCaps.from_simple(width=shape[1], height=shape[0], format="BGR")

        logger.info("Inferred caps from frame: %s", self._gst_caps)


class DuplexShmController(IController):
    """
    Duplex shared memory controller for bidirectional video streaming.

    This controller supports both receiving frames from one buffer and
    sending processed frames to another buffer.
    """

    def __init__(self, connection: ConnectionString):
        """
        Initialize the duplex controller.

        Args:
            connection: Connection string configuration
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
        self._duplex_server: Optional[IImmutableDuplexServer] = None
        self._gst_caps: Optional[GstCaps] = None
        self._metadata: Optional[GstMetadata] = None
        self._is_running = False
        self._on_frame_callback: Optional[Callable[[FrameMetadata, Mat, Mat], None]] = None  # type: ignore[valid-type]
        self._frame_count = 0

    @property
    def is_running(self) -> bool:
        """Check if the controller is running."""
        return self._is_running

    def get_metadata(self) -> Optional[GstMetadata]:
        """Get the current GStreamer metadata."""
        return self._metadata

    def start(
        self,
        on_frame: Callable[[FrameMetadata, Mat, Mat], None],  # type: ignore[override,valid-type]
        cancellation_token: Optional[threading.Event] = None,
    ) -> None:
        """
        Start duplex frame processing with FrameMetadata.

        The callback receives FrameMetadata (frame number, timestamp, dimensions),
        input Mat, and output Mat. The 24-byte metadata prefix is stripped from
        the frame data before creating the input Mat.

        Args:
            on_frame: Callback that receives (FrameMetadata, input_mat, output_mat)
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
        if not self._connection.buffer_name:
            raise ValueError("Buffer name is required for shared memory connection")
        timeout_seconds = self._connection.timeout_ms / 1000.0
        logger.debug(
            "Creating duplex server with timeout: %d ms (%.1f seconds)",
            self._connection.timeout_ms,
            timeout_seconds,
        )
        factory = DuplexChannelFactory()
        self._duplex_server = factory.create_immutable_server(
            self._connection.buffer_name, config, timeout_seconds
        )

        logger.info(
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

        logger.info("Stopping DuplexShmController")
        self._is_running = False

        # Stop the duplex server
        if self._duplex_server:
            self._duplex_server.stop()
            self._duplex_server = None

        logger.info("DuplexShmController stopped")

    def _parse_metadata_json(self, metadata_bytes: bytes | memoryview) -> GstMetadata | None:
        """
        Parse metadata JSON from bytes, handling null padding and boundaries.

        Args:
            metadata_bytes: Raw metadata bytes or memoryview

        Returns:
            GstMetadata object or None if parsing fails
        """
        try:
            # Convert to string
            if isinstance(metadata_bytes, memoryview):
                metadata_bytes = bytes(metadata_bytes)
            metadata_str = metadata_bytes.decode("utf-8")

            # Find JSON boundaries (handle null padding)
            json_start = metadata_str.find("{")
            if json_start < 0:
                logger.debug("No JSON found in metadata")
                return None

            json_end = metadata_str.rfind("}")
            if json_end <= json_start:
                logger.debug("Invalid JSON boundaries in metadata")
                return None

            # Extract JSON
            metadata_str = metadata_str[json_start : json_end + 1]

            # Parse JSON
            metadata_json = json.loads(metadata_str)
            metadata = GstMetadata.from_json(metadata_json)
            return metadata
        except Exception as e:
            logger.debug("Failed to parse metadata JSON: %s", e)
            return None

    def _on_metadata(self, metadata_bytes: bytes | memoryview) -> None:
        """
        Handle metadata from duplex channel.

        Args:
            metadata_bytes: Raw metadata bytes or memoryview
        """
        logger.debug(
            "_on_metadata called with %d bytes", len(metadata_bytes) if metadata_bytes else 0
        )
        try:
            # Log raw bytes for debugging
            logger.debug(
                "Raw metadata bytes (first 100): %r",
                metadata_bytes[: min(100, len(metadata_bytes))],
            )

            # Use helper method to parse metadata
            metadata = self._parse_metadata_json(metadata_bytes)
            if metadata:
                self._metadata = metadata
                self._gst_caps = metadata.caps
                logger.info("Received metadata: %s", self._metadata)
            else:
                logger.warning("Failed to parse metadata from buffer initialization")
        except Exception as e:
            logger.error("Failed to parse metadata: %s", e, exc_info=True)

    def _process_duplex_frame(self, request_frame: Frame, response_writer: Writer) -> None:
        """
        Process a frame in duplex mode with FrameMetadata.

        The frame data has a 24-byte FrameMetadata prefix that is stripped
        before creating the input Mat.

        Args:
            request_frame: Input frame from the request (with metadata prefix)
            response_writer: Writer for the response frame
        """
        try:
            if not self._on_frame_callback:
                logger.warning("No frame callback set")
                return

            # Check frame size is sufficient for metadata
            if request_frame.size < FRAME_METADATA_SIZE:
                logger.warning("Frame too small for FrameMetadata: %d bytes", request_frame.size)
                return

            self._frame_count += 1

            # Parse FrameMetadata from the beginning of the frame
            frame_metadata = FrameMetadata.from_bytes(request_frame.data)

            # Calculate pixel data offset and size
            pixel_data_offset = FRAME_METADATA_SIZE
            pixel_data_size = request_frame.size - FRAME_METADATA_SIZE

            # GstCaps must be available for width/height/format
            # (FrameMetadata no longer contains these - they're stream-level, not per-frame)
            if not self._gst_caps:
                logger.warning(
                    "GstCaps not available, skipping frame %d", frame_metadata.frame_number
                )
                return

            width = self._gst_caps.width
            height = self._gst_caps.height
            format_str = self._gst_caps.format

            # Determine channels from format
            if format_str in ["RGB", "BGR"]:
                channels = 3
            elif format_str in ["RGBA", "BGRA", "ARGB", "ABGR"]:
                channels = 4
            elif format_str in ["GRAY8", "GRAY16_LE", "GRAY16_BE"]:
                channels = 1
            else:
                channels = 3  # Default to RGB

            # Create input Mat from pixel data (after metadata prefix)
            pixel_data = np.frombuffer(request_frame.data[pixel_data_offset:], dtype=np.uint8)

            expected_size = height * width * channels
            if len(pixel_data) != expected_size:
                logger.error(
                    "Pixel data size mismatch. Expected %d bytes for %dx%d with %d channels, got %d",
                    expected_size,
                    width,
                    height,
                    channels,
                    len(pixel_data),
                )
                return

            # Reshape to image dimensions
            if channels == 1:
                input_mat = pixel_data.reshape((height, width))
            else:
                input_mat = pixel_data.reshape((height, width, channels))

            # Response doesn't need metadata prefix - just pixel data
            with response_writer.get_frame_buffer(pixel_data_size) as output_buffer:
                # Create output Mat from buffer (zero-copy)
                output_data = np.frombuffer(output_buffer, dtype=np.uint8)
                if channels == 1:
                    output_mat = output_data.reshape((height, width))
                else:
                    output_mat = output_data.reshape((height, width, channels))

                # Call user's processing function with metadata
                self._on_frame_callback(frame_metadata, input_mat, output_mat)

            # Commit the response frame after buffer is released
            response_writer.commit_frame()

            logger.debug(
                "Processed duplex frame %d (%dx%d %s)",
                frame_metadata.frame_number,
                width,
                height,
                format_str,
            )

        except Exception as e:
            logger.error("Error processing duplex frame: %s", e)

    def _frame_to_mat(self, frame: Frame) -> Optional[Mat]:  # type: ignore[valid-type]
        """Convert frame to OpenCV Mat (reuse from OneWayShmController)."""
        # Implementation is same as OneWayShmController
        return OneWayShmController._create_mat_from_frame(self, frame)  # type: ignore[arg-type]
