"""
High-level API for RocketWelder SDK.

Mirrors C# RocketWelder.SDK API for consistent developer experience.

Example:
    from rocket_welder_sdk.high_level import RocketWelderClient

    with RocketWelderClient.from_environment() as client:
        nose = client.keypoints.define_point("nose")
        person = client.segmentation.define_class(1, "person")
        client.start(process_frame)
"""

from .client import RocketWelderClient, RocketWelderClientOptions
from .connection_strings import (
    KeyPointsConnectionString,
    SegmentationConnectionString,
    VideoSourceConnectionString,
    VideoSourceType,
)
from .data_context import (
    IKeyPointsDataContext,
    ISegmentationDataContext,
)
from .frame_sink_factory import FrameSinkFactory
from .schema import (
    IKeyPointsSchema,
    ISegmentationSchema,
    KeyPointDefinition,
    SegmentClass,
)
from .transport_protocol import (
    TransportKind,
    TransportProtocol,
)

__all__ = [
    "FrameSinkFactory",
    "IKeyPointsDataContext",
    "IKeyPointsSchema",
    "ISegmentationDataContext",
    "ISegmentationSchema",
    "KeyPointDefinition",
    "KeyPointsConnectionString",
    "RocketWelderClient",
    "RocketWelderClientOptions",
    "SegmentClass",
    "SegmentationConnectionString",
    "TransportKind",
    "TransportProtocol",
    "VideoSourceConnectionString",
    "VideoSourceType",
]
