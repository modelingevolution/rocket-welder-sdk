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

if TYPE_CHECKING:
    from .gst_metadata import GstMetadata

# Type alias for OpenCV Mat
Mat = np.ndarray[Any, Any]


class RocketWelderClient:
    """
    Main client for RocketWelder video streaming services.

    Provides a unified interface for different connection types and protocols.
    """

    def __init__(self, connection: str | ConnectionString, logger: logging.Logger | None = None):
        """
        Initialize the RocketWelder client.

        Args:
            connection: Connection string or ConnectionString object
            logger: Optional logger instance
        """
        if isinstance(connection, str):
            self._connection = ConnectionString.parse(connection)
        else:
            self._connection = connection

        self._logger = logger or logging.getLogger(__name__)
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
                    self._controller = DuplexShmController(self._connection, self._logger)
                else:
                    self._controller = OneWayShmController(self._connection, self._logger)
            else:
                raise ValueError(f"Unsupported protocol: {self._connection.protocol}")

            # Start the controller
            self._controller.start(on_frame, cancellation_token)  # type: ignore[arg-type]
            self._logger.info("RocketWelder client started with %s", self._connection)

    def stop(self) -> None:
        """Stop the client and clean up resources."""
        with self._lock:
            if self._controller:
                self._controller.stop()
                self._controller = None
                self._logger.info("RocketWelder client stopped")

    def __enter__(self) -> RocketWelderClient:
        """Context manager entry."""
        return self

    def __exit__(self, exc_type: Any, exc_val: Any, exc_tb: Any) -> None:
        """Context manager exit."""
        self.stop()

    @classmethod
    def create_oneway_shm(
        cls,
        buffer_name: str,
        buffer_size: str = "256MB",
        metadata_size: str = "4KB",
        logger: logging.Logger | None = None,
    ) -> RocketWelderClient:
        """
        Create a one-way shared memory client.

        Args:
            buffer_name: Name of the shared memory buffer
            buffer_size: Size of the buffer (e.g., "256MB")
            metadata_size: Size of metadata buffer (e.g., "4KB")
            logger: Optional logger

        Returns:
            Configured RocketWelderClient instance
        """
        connection_str = (
            f"shm://{buffer_name}?size={buffer_size}&metadata={metadata_size}&mode=OneWay"
        )
        return cls(connection_str, logger)

    @classmethod
    def create_duplex_shm(
        cls,
        buffer_name: str,
        buffer_size: str = "256MB",
        metadata_size: str = "4KB",
        logger: logging.Logger | None = None,
    ) -> RocketWelderClient:
        """
        Create a duplex shared memory client.

        Args:
            buffer_name: Name of the shared memory buffer
            buffer_size: Size of the buffer (e.g., "256MB")
            metadata_size: Size of metadata buffer (e.g., "4KB")
            logger: Optional logger

        Returns:
            Configured RocketWelderClient instance
        """
        connection_str = (
            f"shm://{buffer_name}?size={buffer_size}&metadata={metadata_size}&mode=Duplex"
        )
        return cls(connection_str, logger)
