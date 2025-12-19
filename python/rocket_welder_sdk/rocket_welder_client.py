"""
Enterprise-grade RocketWelder client for video streaming.
Main entry point for the RocketWelder SDK.
"""

from __future__ import annotations

import logging
import queue
import threading
from typing import TYPE_CHECKING, Any, Callable, List, Optional, Union

import numpy as np

from .connection_string import ConnectionMode, ConnectionString, Protocol
from .controllers import DuplexShmController, IController, OneWayShmController
from .frame_metadata import FrameMetadata  # noqa: TC001 - used at runtime in callbacks
from .opencv_controller import OpenCvController
from .session_id import (
    get_configured_nng_urls,
    get_nng_urls_from_env,
    has_explicit_nng_urls,
)
from .transport.nng_transport import NngFrameSink

if TYPE_CHECKING:
    import numpy.typing as npt

    from .gst_metadata import GstMetadata

    # Use numpy array type for Mat - OpenCV Mat is essentially a numpy array
    Mat = npt.NDArray[np.uint8]
else:
    Mat = np.ndarray  # type: ignore[misc]

# Module logger
logger = logging.getLogger(__name__)


class RocketWelderClient:
    """
    Main client for RocketWelder video streaming services.

    Provides a unified interface for different connection types and protocols.
    """

    def __init__(self, connection: Union[str, ConnectionString]):
        """
        Initialize the RocketWelder client.

        Args:
            connection: Connection string or ConnectionString object
        """
        if isinstance(connection, str):
            self._connection = ConnectionString.parse(connection)
        else:
            self._connection = connection

        self._controller: Optional[IController] = None
        self._lock = threading.Lock()

        # NNG publishers for streaming results (auto-created if SessionId env var is set)
        self._nng_publishers: dict[str, NngFrameSink] = {}

        # Preview support
        self._preview_enabled = (
            self._connection.parameters.get("preview", "false").lower() == "true"
        )
        self._preview_queue: queue.Queue[Optional[Mat]] = queue.Queue(maxsize=2)  # type: ignore[valid-type]  # Small buffer
        self._preview_window_name = "RocketWelder Preview"
        self._original_callback: Any = None

    @property
    def connection(self) -> ConnectionString:
        """Get the connection configuration."""
        return self._connection

    @property
    def is_running(self) -> bool:
        """Check if the client is running."""
        with self._lock:
            return self._controller is not None and self._controller.is_running

    @property
    def nng_publishers(self) -> dict[str, NngFrameSink]:
        """Get NNG publishers for streaming results.

        Returns:
            Dictionary with 'segmentation', 'keypoints', 'actions' keys.
            Empty if SessionId env var was not set at startup.

        Example:
            client.nng_publishers["segmentation"].write_frame(seg_data)
        """
        return self._nng_publishers

    def _create_nng_publishers(self) -> None:
        """Create NNG publishers for result streaming.

        URLs are read from environment variables (preferred) or derived from SessionId (fallback).

        Priority:
        1. Explicit URLs: SEGMENTATION_SINK_URL, KEYPOINTS_SINK_URL, ACTIONS_SINK_URL
        2. Derived from SessionId environment variable (backwards compatibility)
        """
        try:
            urls = get_configured_nng_urls()

            for name, url in urls.items():
                sink = NngFrameSink.create_publisher(url)
                self._nng_publishers[name] = sink
                logger.info("NNG publisher ready: %s at %s", name, url)

            # Log configuration summary
            logger.info(
                "NNG publishers configured: seg=%s, kp=%s, actions=%s",
                urls.get("segmentation", "(not configured)"),
                urls.get("keypoints", "(not configured)"),
                urls.get("actions", "(not configured)"),
            )
        except ValueError as ex:
            # No URLs configured - this is expected for containers that don't publish results
            logger.debug("NNG publishers not configured: %s", ex)
        except Exception as ex:
            logger.warning("Failed to create NNG publishers: %s", ex)
            # Don't fail start() - NNG is optional for backwards compatibility

    def get_metadata(self) -> Optional[GstMetadata]:
        """
        Get the current GStreamer metadata.

        Returns:
            GstMetadata or None if not available
        """
        with self._lock:
            if self._controller:
                return self._controller.get_metadata()
            return None

    def start(
        self,
        on_frame: Union[Callable[[Mat], None], Callable[[Mat, Mat], None]],  # type: ignore[valid-type]
        cancellation_token: Optional[threading.Event] = None,
    ) -> None:
        """
        Start receiving/processing video frames.

        Args:
            on_frame: Callback for frame processing.
                     For one-way: (input_frame) -> None
                     For duplex: (input_frame, output_frame) -> None
            cancellation_token: Optional cancellation token

        Raises:
            RuntimeError: If already running
            ValueError: If connection type is not supported
        """
        with self._lock:
            if self._controller and self._controller.is_running:
                raise RuntimeError("Client is already running")

            # Create appropriate controller based on connection
            if self._connection.protocol == Protocol.SHM:
                if self._connection.connection_mode == ConnectionMode.DUPLEX:
                    self._controller = DuplexShmController(self._connection)
                else:
                    self._controller = OneWayShmController(self._connection)
            elif self._connection.protocol == Protocol.FILE or bool(
                self._connection.protocol & Protocol.MJPEG  # type: ignore[operator]
            ):
                self._controller = OpenCvController(self._connection)
            else:
                raise ValueError(f"Unsupported protocol: {self._connection.protocol}")

            # Auto-create NNG publishers if URLs are configured
            # (explicit URLs via SEGMENTATION_SINK_URL etc., or derived from SessionId)
            if has_explicit_nng_urls():
                self._create_nng_publishers()
            else:
                # Log that NNG is not configured (informational)
                urls = get_nng_urls_from_env()
                logger.info(
                    "NNG sink URLs not configured (this is normal if not publishing AI results). "
                    "seg=%s, kp=%s, actions=%s",
                    urls.get("segmentation") or "(not set)",
                    urls.get("keypoints") or "(not set)",
                    urls.get("actions") or "(not set)",
                )

            # If preview is enabled, wrap the callback to capture frames
            if self._preview_enabled:
                self._original_callback = on_frame

                # Determine if duplex or one-way
                if self._connection.connection_mode == ConnectionMode.DUPLEX:

                    def preview_wrapper_duplex(
                        metadata: FrameMetadata, input_frame: Mat, output_frame: Mat  # type: ignore[valid-type]
                    ) -> None:
                        # Call original callback (ignoring FrameMetadata for backwards compatibility)
                        on_frame(input_frame, output_frame)  # type: ignore[call-arg]
                        # Queue the OUTPUT frame for preview
                        try:
                            self._preview_queue.put_nowait(output_frame.copy())  # type: ignore[attr-defined]
                        except queue.Full:
                            # Drop oldest frame if queue is full
                            try:
                                self._preview_queue.get_nowait()
                                self._preview_queue.put_nowait(output_frame.copy())  # type: ignore[attr-defined]
                            except queue.Empty:
                                pass

                    actual_callback = preview_wrapper_duplex
                else:

                    def preview_wrapper_oneway(frame: Mat) -> None:  # type: ignore[valid-type]
                        # Call original callback
                        on_frame(frame)  # type: ignore[call-arg]
                        # Queue frame for preview
                        try:
                            self._preview_queue.put_nowait(frame.copy())  # type: ignore[attr-defined]
                        except queue.Full:
                            # Drop oldest frame if queue is full
                            try:
                                self._preview_queue.get_nowait()
                                self._preview_queue.put_nowait(frame.copy())  # type: ignore[attr-defined]
                            except queue.Empty:
                                pass

                    actual_callback = preview_wrapper_oneway  # type: ignore[assignment]
            else:
                # Wrap the callback to adapt (Mat, Mat) -> (FrameMetadata, Mat, Mat) for duplex
                if self._connection.connection_mode == ConnectionMode.DUPLEX:

                    def metadata_adapter(
                        metadata: FrameMetadata, input_frame: Mat, output_frame: Mat  # type: ignore[valid-type]
                    ) -> None:
                        # Call original callback (ignoring FrameMetadata for backwards compatibility)
                        on_frame(input_frame, output_frame)  # type: ignore[call-arg]

                    actual_callback = metadata_adapter
                else:
                    actual_callback = on_frame  # type: ignore[assignment]

            # Start the controller
            self._controller.start(actual_callback, cancellation_token)  # type: ignore[arg-type]
            logger.info("RocketWelder client started with %s", self._connection)

    def stop(self) -> None:
        """Stop the client and clean up resources."""
        with self._lock:
            if self._controller:
                self._controller.stop()
                self._controller = None

                # Signal preview to stop if enabled
                if self._preview_enabled:
                    self._preview_queue.put(None)  # Sentinel value

                # Clean up NNG publishers
                for name, sink in self._nng_publishers.items():
                    try:
                        sink.close()
                        logger.debug("Closed NNG publisher: %s", name)
                    except Exception as ex:
                        logger.warning("Failed to close NNG publisher %s: %s", name, ex)
                self._nng_publishers.clear()

                logger.info("RocketWelder client stopped")

    def show(self, cancellation_token: Optional[threading.Event] = None) -> None:
        """
        Display preview frames in a window (main thread only).

        This method should be called from the main thread after start().
        - If preview=true: blocks and displays frames until stopped or 'q' pressed
        - If preview=false or not set: returns immediately

        Args:
            cancellation_token: Optional cancellation token to stop preview

        Example:
            client = RocketWelderClient("file:///video.mp4?preview=true")
            client.start(process_frame)
            client.show()  # Blocks and shows preview
            client.stop()
        """
        if not self._preview_enabled:
            # No preview requested, return immediately
            return

        try:
            import cv2
        except ImportError:
            logger.warning("OpenCV not available, cannot show preview")
            return

        logger.info("Starting preview display in main thread")

        # Create window
        cv2.namedWindow(self._preview_window_name, cv2.WINDOW_NORMAL)

        try:
            while True:
                # Check for cancellation
                if cancellation_token and cancellation_token.is_set():
                    break

                try:
                    # Get frame with timeout
                    frame = self._preview_queue.get(timeout=0.1)

                    # Check for stop sentinel
                    if frame is None:
                        break

                    # Display frame
                    cv2.imshow(self._preview_window_name, frame)

                    # Process window events and check for 'q' key
                    key = cv2.waitKey(1) & 0xFF
                    if key == ord("q"):
                        logger.info("User pressed 'q', stopping preview")
                        break

                except queue.Empty:
                    # No frame available, check if still running
                    if not self.is_running:
                        break
                    # Process window events even without new frame
                    if cv2.waitKey(1) & 0xFF == ord("q"):
                        logger.info("User pressed 'q', stopping preview")
                        break

        finally:
            # Clean up window
            cv2.destroyWindow(self._preview_window_name)
            cv2.waitKey(1)  # Process pending events
            logger.info("Preview display stopped")

    def __enter__(self) -> RocketWelderClient:
        """Context manager entry."""
        return self

    def __exit__(self, exc_type: Any, exc_val: Any, exc_tb: Any) -> None:
        """Context manager exit."""
        self.stop()

    @classmethod
    def from_connection_string(cls, connection_string: str) -> RocketWelderClient:
        """
        Create a client from a connection string.

        Args:
            connection_string: Connection string (e.g., 'shm://buffer?mode=Duplex')

        Returns:
            Configured RocketWelderClient instance
        """
        return cls(connection_string)

    @classmethod
    def from_args(cls, args: List[str]) -> RocketWelderClient:
        """
        Create a client from command line arguments.

        Checks in order:
        1. First positional argument from args
        2. CONNECTION_STRING environment variable

        Args:
            args: Command line arguments (typically sys.argv)

        Returns:
            Configured RocketWelderClient instance

        Raises:
            ValueError: If no connection string is found
        """
        import os

        # Check for positional argument (skip script name if present)
        connection_string = None
        for arg in args[1:] if len(args) > 0 and args[0].endswith(".py") else args:
            if not arg.startswith("-"):
                connection_string = arg
                break

        # Fall back to environment variable
        if not connection_string:
            connection_string = os.environ.get("CONNECTION_STRING")

        if not connection_string:
            raise ValueError(
                "No connection string provided. "
                "Provide as argument or set CONNECTION_STRING environment variable"
            )

        return cls(connection_string)

    @classmethod
    def from_(cls, *args: Any, **kwargs: Any) -> RocketWelderClient:
        """
        Create a client with automatic configuration detection.

        This is the most convenient factory method that:
        1. Checks kwargs for 'args' parameter (command line arguments)
        2. Checks args for command line arguments
        3. Falls back to CONNECTION_STRING environment variable

        Examples:
            client = RocketWelderClient.from_()  # Uses env var
            client = RocketWelderClient.from_(sys.argv)  # Uses command line
            client = RocketWelderClient.from_(args=sys.argv)  # Named param

        Returns:
            Configured RocketWelderClient instance

        Raises:
            ValueError: If no connection string is found
        """
        import os

        # Check kwargs first
        argv = kwargs.get("args")

        # Then check positional args
        if not argv and args:
            # If first arg looks like sys.argv (list), use it
            if isinstance(args[0], list):
                argv = args[0]
            # If first arg is a string, treat it as connection string
            elif isinstance(args[0], str):
                return cls(args[0])

        # Try to get from command line args if provided
        if argv:
            try:
                return cls.from_args(argv)
            except ValueError:
                pass  # Fall through to env var check

        # Fall back to environment variable
        connection_string = os.environ.get("CONNECTION_STRING")
        if connection_string:
            return cls(connection_string)

        raise ValueError(
            "No connection string provided. "
            "Provide as argument or set CONNECTION_STRING environment variable"
        )

    @classmethod
    def create_oneway_shm(
        cls,
        buffer_name: str,
        buffer_size: str = "256MB",
        metadata_size: str = "4KB",
    ) -> RocketWelderClient:
        """
        Create a one-way shared memory client.

        Args:
            buffer_name: Name of the shared memory buffer
            buffer_size: Size of the buffer (e.g., "256MB")
            metadata_size: Size of metadata buffer (e.g., "4KB")

        Returns:
            Configured RocketWelderClient instance
        """
        connection_str = (
            f"shm://{buffer_name}?size={buffer_size}&metadata={metadata_size}&mode=OneWay"
        )
        return cls(connection_str)

    @classmethod
    def create_duplex_shm(
        cls,
        buffer_name: str,
        buffer_size: str = "256MB",
        metadata_size: str = "4KB",
    ) -> RocketWelderClient:
        """
        Create a duplex shared memory client.

        Args:
            buffer_name: Name of the shared memory buffer
            buffer_size: Size of the buffer (e.g., "256MB")
            metadata_size: Size of metadata buffer (e.g., "4KB")

        Returns:
            Configured RocketWelderClient instance
        """
        connection_str = (
            f"shm://{buffer_name}?size={buffer_size}&metadata={metadata_size}&mode=Duplex"
        )
        return cls(connection_str)
