"""
High-level API for RocketWelder SDK.

Provides a simplified, user-friendly API for common video processing workflows
with automatic transport management and schema definitions.

Example:
    from rocket_welder_sdk.high_level import RocketWelderClient, Transport

    async with RocketWelderClient.from_environment() as client:
        # Define keypoints schema
        nose = client.keypoints.define_point("nose")
        left_eye = client.keypoints.define_point("left_eye")

        # Define segmentation classes
        person = client.segmentation.define_class(1, "person")

        async for input_frame, seg_ctx, kp_ctx, output_frame in client.start():
            # Process frame...
            kp_ctx.add(nose, x=100, y=200, confidence=0.95)
            seg_ctx.add(person, instance_id=0, points=contour_points)
"""

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
from .schema import (
    IKeyPointsSchema,
    ISegmentationSchema,
    KeyPoint,
    SegmentClass,
)
from .transport_protocol import (
    MessagingLibrary,
    MessagingPattern,
    Transport,
    TransportBuilder,
    TransportLayer,
    TransportProtocol,
)

__all__ = [
    "IKeyPointsDataContext",
    "IKeyPointsSchema",
    "ISegmentationDataContext",
    "ISegmentationSchema",
    "KeyPoint",
    "KeyPointsConnectionString",
    "MessagingLibrary",
    "MessagingPattern",
    "SegmentClass",
    "SegmentationConnectionString",
    "Transport",
    "TransportBuilder",
    "TransportLayer",
    "TransportProtocol",
    "VideoSourceConnectionString",
    "VideoSourceType",
]
