"""
Enterprise-grade RocketWelder client for video streaming.
Main entry point for the RocketWelder SDK.
"""

from __future__ import annotations

import logging
import threading
from typing import TYPE_CHECKING, Any, Callable

import numpy as np

from .connection_string import ConnectionMode, ConnectionString, Protocol
from .controllers import DuplexShmController, IController, OneWayShmController
from .opencv_controller import OpenCvController

if TYPE_CHECKING:
    from .gst_metadata import GstMetadata

# Type alias for OpenCV Mat
Mat = np.ndarray[Any, Any]

# Module logger
logger = logging.getLogger(__name__)


class RocketWelderClient:
    """
    Main client for RocketWelder video streaming services.

    Provides a unified interface for different connection types and protocols.
    """

    def __init__(self, connection: str | ConnectionString):
        """
        Initialize the RocketWelder client.

        Args:
            connection: Connection string or ConnectionString object
        """
        if isinstance(connection, str):
            self._connection = ConnectionString.parse(connection)
        else:
            self._connection = connection

        self._controller: IController | None = None
        self._lock = threading.Lock()

    @property
    def connection(self) -> ConnectionString:
        """Get the connection configuration."""
        return self._connection

    @property
    def is_running(self) -> bool:
        """Check if the client is running."""
        with self._lock:
            return self._controller is not None and self._controller.is_running

    def get_metadata(self) -> GstMetadata | None:
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
        on_frame: Callable[[Mat], None] | Callable[[Mat, Mat], None],
        cancellation_token: threading.Event | None = None,
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
            elif self._connection.protocol in (Protocol.FILE, Protocol.MJPEG):
                self._controller = OpenCvController(self._connection)
            else:
                raise ValueError(f"Unsupported protocol: {self._connection.protocol}")

            # Start the controller
            self._controller.start(on_frame, cancellation_token)  # type: ignore[arg-type]
            logger.info("RocketWelder client started with %s", self._connection)

    def stop(self) -> None:
        """Stop the client and clean up resources."""
        with self._lock:
            if self._controller:
                self._controller.stop()
                self._controller = None
                logger.info("RocketWelder client stopped")

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
    def from_args(cls, args: list[str]) -> RocketWelderClient:
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
