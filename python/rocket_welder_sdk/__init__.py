"""
RocketWelder SDK - Enterprise-grade Python client library for video streaming services.

High-performance video streaming using shared memory (ZeroBuffer) for zero-copy operations.
"""

from .bytes_size import BytesSize
from .connection_string import ConnectionMode, ConnectionString, Protocol
from .controllers import DuplexShmController, IController, OneWayShmController
from .gst_metadata import GstCaps, GstMetadata
from .rocket_welder_client import RocketWelderClient

# Alias for backward compatibility
Client = RocketWelderClient

__version__ = "1.1.0"

__all__ = [
    # Core types
    "BytesSize",
    "Client",  # Backward compatibility
    "ConnectionMode",
    "ConnectionString",
    "DuplexShmController",
    # GStreamer metadata
    "GstCaps",
    "GstMetadata",
    # Controllers
    "IController",
    "OneWayShmController",
    "Protocol",
    # Main client
    "RocketWelderClient",
]
