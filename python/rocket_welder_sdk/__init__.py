"""
RocketWelder SDK - Enterprise-grade Python client library for video streaming services.

High-performance video streaming using shared memory (ZeroBuffer) for zero-copy operations.
"""

import logging
import os

from .binary_frame_reader import BinaryFrameReader
from .binary_frame_writer import BinaryFrameWriter
from .bytes_size import BytesSize
from .confidence import Confidence
from .connection_string import ConnectionMode, ConnectionString, Protocol
from .controllers import DuplexShmController, IController, OneWayShmController
from .delta_frame import DeltaFrame
from .frame_metadata import FRAME_METADATA_SIZE, FrameMetadata, GstVideoFormat
from .graphics import (
    FrameType,
    ILayerCanvas,
    IStageSink,
    IStageWriter,
    LayerEncoder,
    OpType,
    PropertyId,
    RgbColor,
    StageSink,
    StageWriter,
    VectorGraphicsEncoder,
)
from .gst_metadata import GstCaps, GstMetadata
from .keypoints_protocol import (
    IKeyPointsSink,
    IKeyPointsWriter,
    KeyPointsSink,
    KeyPointsWriter,
)
from .opencv_controller import OpenCvController
from .periodic_timer import PeriodicTimer, PeriodicTimerSync
from .rocket_welder_client import RocketWelderClient
from .segmentation_result import (
    ISegmentationResultSink,
    ISegmentationResultSource,
    ISegmentationResultWriter,
    SegmentationFrame,
    SegmentationProtocol,
    SegmentationResultSink,
    SegmentationResultSource,
    SegmentationResultWriter,
)
from .session_id import (
    # Explicit URL environment variable names (set by rocket-welder2)
    ACTIONS_SINK_URL_ENV,
    GRAPHICS_SINK_URL_ENV,
    KEYPOINTS_SINK_URL_ENV,
    SEGMENTATION_SINK_URL_ENV,
    # SessionId parsing
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
    "ACTIONS_SINK_URL_ENV",
    "FRAME_METADATA_SIZE",
    "GRAPHICS_SINK_URL_ENV",
    "KEYPOINTS_SINK_URL_ENV",
    "SEGMENTATION_SINK_URL_ENV",
    "BinaryFrameReader",
    "BinaryFrameWriter",
    "BytesSize",
    "Client",
    "Confidence",
    "ConnectionMode",
    "ConnectionString",
    "DeltaFrame",
    "DuplexShmController",
    "FrameMetadata",
    "FrameType",
    "GstCaps",
    "GstMetadata",
    "GstVideoFormat",
    "IController",
    "IKeyPointsSink",
    "IKeyPointsWriter",
    "ILayerCanvas",
    "ISegmentationResultSink",
    "ISegmentationResultSource",
    "ISegmentationResultWriter",
    "IStageSink",
    "IStageWriter",
    "KeyPointsSink",
    "KeyPointsWriter",
    "LayerEncoder",
    "OneWayShmController",
    "OpType",
    "OpenCvController",
    "PeriodicTimer",
    "PeriodicTimerSync",
    "PropertyId",
    "Protocol",
    "RgbColor",
    "RocketWelderClient",
    "SegmentationFrame",
    "SegmentationProtocol",
    "SegmentationResultSink",
    "SegmentationResultSource",
    "SegmentationResultWriter",
    "StageSink",
    "StageWriter",
    "VectorGraphicsEncoder",
    "get_session_id_from_env",
    "parse_session_id",
]
