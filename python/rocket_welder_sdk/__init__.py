"""
RocketWelder SDK - Enterprise-grade Python client library for video streaming services.

High-performance video streaming using shared memory (ZeroBuffer) for zero-copy operations.
"""

import logging
import os

from .bytes_size import BytesSize
from .connection_string import ConnectionMode, ConnectionString, Protocol
from .controllers import DuplexShmController, IController, OneWayShmController
from .frame_metadata import FRAME_METADATA_SIZE, FrameMetadata, GstVideoFormat
from .gst_metadata import GstCaps, GstMetadata
from .opencv_controller import OpenCvController
from .periodic_timer import PeriodicTimer, PeriodicTimerSync
from .rocket_welder_client import RocketWelderClient
from .session_id import (
    get_actions_url,
    get_keypoints_url,
    get_nng_urls,
    get_segmentation_url,
    get_session_id_from_env,
    parse_session_id,
)

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
    "FRAME_METADATA_SIZE",
    "BytesSize",
    "Client",
    "ConnectionMode",
    "ConnectionString",
    "DuplexShmController",
    "FrameMetadata",
    "GstCaps",
    "GstMetadata",
    "GstVideoFormat",
    "IController",
    "OneWayShmController",
    "OpenCvController",
    "PeriodicTimer",
    "PeriodicTimerSync",
    "Protocol",
    "RocketWelderClient",
    # SessionId utilities for NNG URL generation
    "get_actions_url",
    "get_keypoints_url",
    "get_nng_urls",
    "get_segmentation_url",
    "get_session_id_from_env",
    "parse_session_id",
]
