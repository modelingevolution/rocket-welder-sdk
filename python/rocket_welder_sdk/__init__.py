"""
RocketWelder SDK - Enterprise-grade Python client library for video streaming services.

High-performance video streaming using shared memory (ZeroBuffer) for zero-copy operations.
"""

import logging
import os

from .bytes_size import BytesSize
from .connection_string import ConnectionMode, ConnectionString, Protocol
from .controllers import DuplexShmController, IController, OneWayShmController
from .gst_metadata import GstCaps, GstMetadata
from .rocket_welder_client import RocketWelderClient

# Alias for backward compatibility and README examples
Client = RocketWelderClient

__version__ = "1.1.0"

# Configure library logger with NullHandler (best practice for libraries)
logging.getLogger(__name__).addHandler(logging.NullHandler())

# Configure from environment variable and propagate to zerobuffer
_log_level = os.environ.get("ROCKET_WELDER_LOG_LEVEL")
if _log_level:
    try:
        # Set rocket-welder-sdk log level
        logging.getLogger(__name__).setLevel(getattr(logging, _log_level.upper()))

        # Propagate to zerobuffer if not already set
        if not os.environ.get("ZEROBUFFER_LOG_LEVEL"):
            os.environ["ZEROBUFFER_LOG_LEVEL"] = _log_level
            # Also configure zerobuffer logger if already imported
            zerobuffer_logger = logging.getLogger("zerobuffer")
            zerobuffer_logger.setLevel(getattr(logging, _log_level.upper()))
    except AttributeError:
        pass  # Invalid log level, ignore

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
