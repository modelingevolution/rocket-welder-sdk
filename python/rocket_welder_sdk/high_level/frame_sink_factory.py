"""
Factory for creating IFrameSink instances from parsed protocol and address.

Does NOT parse URLs - use SegmentationConnectionString or KeyPointsConnectionString for parsing.

This mirrors the C# FrameSinkFactory class for API consistency.

Usage:
    from rocket_welder_sdk.high_level import FrameSinkFactory, SegmentationConnectionString

    cs = SegmentationConnectionString.parse("socket:///tmp/seg.sock")
    sink = FrameSinkFactory.create(cs.protocol, cs.address)

    # For null sink (no output configured):
    sink = FrameSinkFactory.create_null()
"""

from __future__ import annotations

import logging
from typing import TYPE_CHECKING, Optional

from .transport_protocol import TransportProtocol

if TYPE_CHECKING:
    from rocket_welder_sdk.transport.frame_sink import IFrameSink

logger = logging.getLogger(__name__)


class FrameSinkFactory:
    """
    Factory for creating IFrameSink instances from parsed protocol and address.

    Does NOT parse URLs - use SegmentationConnectionString or KeyPointsConnectionString for parsing.

    Mirrors C# RocketWelder.SDK.Transport.FrameSinkFactory.
    """

    @staticmethod
    def create(
        protocol: Optional[TransportProtocol],
        address: str,
        *,
        logger_instance: Optional[logging.Logger] = None,
    ) -> IFrameSink:
        """
        Create a frame sink from parsed protocol and address.

        Returns NullFrameSink if protocol is None (no URL specified).

        Args:
            protocol: The transport protocol (from ConnectionString.protocol), or None
            address: The address (file path, socket path, or NNG address)
            logger_instance: Optional logger for diagnostics

        Returns:
            An IFrameSink connected to the specified address, or NullFrameSink if protocol is None

        Raises:
            ValueError: If protocol is not supported for sinks

        Example:
            cs = SegmentationConnectionString.parse("socket:///tmp/seg.sock")
            sink = FrameSinkFactory.create(cs.protocol, cs.address)
        """
        from rocket_welder_sdk.transport import NngFrameSink, NullFrameSink
        from rocket_welder_sdk.transport.stream_transport import StreamFrameSink
        from rocket_welder_sdk.transport.unix_socket_transport import UnixSocketFrameSink

        log = logger_instance or logger

        # Handle None protocol - return null sink
        if protocol is None:
            log.debug("No protocol specified, using NullFrameSink")
            return NullFrameSink.instance()

        if not isinstance(protocol, TransportProtocol):
            raise TypeError(f"Expected TransportProtocol, got {type(protocol).__name__}")

        if protocol.is_file:
            log.info("Creating file frame sink at: %s", address)
            file_handle = open(address, "wb")  # noqa: SIM115
            return StreamFrameSink(file_handle)

        if protocol.is_socket:
            log.info("Creating Unix socket frame sink at: %s", address)
            return UnixSocketFrameSink.connect(address)

        if protocol.is_nng:
            log.info("Creating NNG frame sink (%s) at: %s", protocol.schema, address)

            if protocol.is_pub:
                return NngFrameSink.create_publisher(address)
            if protocol.is_push:
                return NngFrameSink.create_pusher(address)

            raise ValueError(
                f"NNG protocol '{protocol.schema}' is not supported for sinks "
                "(only pub and push are supported)"
            )

        raise ValueError(f"Transport protocol '{protocol.schema}' is not supported for frame sinks")

    @staticmethod
    def create_null() -> IFrameSink:
        """
        Create a null frame sink that discards all data.

        Use when no output URL is configured.
        """
        from rocket_welder_sdk.transport import NullFrameSink

        return NullFrameSink.instance()


# Re-export for convenience
__all__ = ["FrameSinkFactory"]
