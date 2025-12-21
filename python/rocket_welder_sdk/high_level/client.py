"""
RocketWelderClient - High-level API matching C# RocketWelder.SDK.

Usage:
    with RocketWelderClient.from_environment() as client:
        # Define schema
        nose = client.keypoints.define_point("nose")
        person = client.segmentation.define_class(1, "person")

        # Start processing
        client.start(process_frame)
"""

from __future__ import annotations

import logging
from dataclasses import dataclass, field
from typing import TYPE_CHECKING, Any, Callable, Optional

import numpy as np
import numpy.typing as npt
from typing_extensions import TypeAlias

from .connection_strings import (
    KeyPointsConnectionString,
    SegmentationConnectionString,
    VideoSourceConnectionString,
)
from .data_context import (
    IKeyPointsDataContext,
    ISegmentationDataContext,
    KeyPointsDataContext,
    SegmentationDataContext,
)
from .frame_sink_factory import FrameSinkFactory
from .schema import (
    IKeyPointsSchema,
    ISegmentationSchema,
    KeyPointsSchema,
    SegmentationSchema,
)

if TYPE_CHECKING:
    from rocket_welder_sdk.keypoints_protocol import KeyPointsSink
    from rocket_welder_sdk.transport.frame_sink import IFrameSink

# Type alias for OpenCV Mat (numpy array)
Mat: TypeAlias = npt.NDArray[np.uint8]

logger = logging.getLogger(__name__)


@dataclass
class RocketWelderClientOptions:
    """Configuration options for RocketWelderClient."""

    video_source: VideoSourceConnectionString = field(
        default_factory=VideoSourceConnectionString.default
    )
    keypoints: KeyPointsConnectionString = field(default_factory=KeyPointsConnectionString.default)
    segmentation: SegmentationConnectionString = field(
        default_factory=SegmentationConnectionString.default
    )

    @classmethod
    def from_environment(cls) -> RocketWelderClientOptions:
        """Create from environment variables."""
        return cls(
            video_source=VideoSourceConnectionString.from_environment(),
            keypoints=KeyPointsConnectionString.from_environment(),
            segmentation=SegmentationConnectionString.from_environment(),
        )


class RocketWelderClient:
    """
    High-level client for RocketWelder SDK.

    Mirrors C# RocketWelder.SDK.IRocketWelderClient interface.
    """

    def __init__(self, options: RocketWelderClientOptions) -> None:
        self._options = options
        self._keypoints_schema = KeyPointsSchema()
        self._segmentation_schema = SegmentationSchema()
        self._keypoints_sink: Optional[KeyPointsSink] = None
        self._keypoints_frame_sink: Optional[IFrameSink] = None
        self._segmentation_frame_sink: Optional[IFrameSink] = None
        self._closed = False
        logger.debug("RocketWelderClient created with options: %s", options)

    @classmethod
    def from_environment(cls) -> RocketWelderClient:
        """Create client from environment variables."""
        logger.info("Creating RocketWelderClient from environment variables")
        return cls(RocketWelderClientOptions.from_environment())

    @classmethod
    def create(cls, options: Optional[RocketWelderClientOptions] = None) -> RocketWelderClient:
        """Create client with explicit options."""
        return cls(options or RocketWelderClientOptions())

    @property
    def keypoints(self) -> IKeyPointsSchema:
        """Schema for defining keypoints."""
        return self._keypoints_schema

    @property
    def segmentation(self) -> ISegmentationSchema:
        """Schema for defining segmentation classes."""
        return self._segmentation_schema

    def start(
        self,
        process_frame: Callable[[Mat, ISegmentationDataContext, IKeyPointsDataContext, Mat], None],
    ) -> None:
        """Start with both keypoints and segmentation."""
        self._run_loop(process_frame, use_keypoints=True, use_segmentation=True)

    def start_keypoints(
        self,
        process_frame: Callable[[Mat, IKeyPointsDataContext, Mat], None],
    ) -> None:
        """Start with keypoints only."""
        self._run_loop(process_frame, use_keypoints=True, use_segmentation=False)

    def start_segmentation(
        self,
        process_frame: Callable[[Mat, ISegmentationDataContext, Mat], None],
    ) -> None:
        """Start with segmentation only."""
        self._run_loop(process_frame, use_keypoints=False, use_segmentation=True)

    def _run_loop(
        self,
        process_frame: Callable[..., None],
        use_keypoints: bool,
        use_segmentation: bool,
    ) -> None:
        """Run processing loop."""
        from rocket_welder_sdk.keypoints_protocol import KeyPointsSink

        logger.info(
            "Starting processing loop (keypoints=%s, segmentation=%s)",
            use_keypoints,
            use_segmentation,
        )

        # Initialize sinks
        if use_keypoints:
            cs = self._options.keypoints
            logger.info("Initializing keypoints sink: %s -> %s", cs.protocol, cs.address)
            self._keypoints_frame_sink = self._create_frame_sink(cs.protocol, cs.address)
            self._keypoints_sink = KeyPointsSink(
                frame_sink=self._keypoints_frame_sink,
                master_frame_interval=cs.master_frame_interval,
                owns_sink=False,  # We manage frame sink lifecycle in close()
            )
            logger.debug(
                "KeyPointsSink created with master_frame_interval=%d", cs.master_frame_interval
            )

        if use_segmentation:
            seg_cs = self._options.segmentation
            logger.info("Initializing segmentation sink: %s -> %s", seg_cs.protocol, seg_cs.address)
            self._segmentation_frame_sink = self._create_frame_sink(seg_cs.protocol, seg_cs.address)
            logger.debug("Segmentation frame sink created")

        # TODO: Video capture loop - for now raise NotImplementedError
        raise NotImplementedError(
            "Video capture not implemented. Use process_frame_sync() or low-level API."
        )

    def process_frame_sync(
        self,
        frame_id: int,
        input_frame: Mat,
        output_frame: Mat,
        width: int,
        height: int,
    ) -> tuple[Optional[IKeyPointsDataContext], Optional[ISegmentationDataContext]]:
        """
        Process a single frame synchronously.

        Returns (keypoints_context, segmentation_context) for the caller to use.
        Caller must call commit() on contexts when done.
        """
        from rocket_welder_sdk.segmentation_result import SegmentationResultWriter

        kp_ctx: Optional[IKeyPointsDataContext] = None
        seg_ctx: Optional[ISegmentationDataContext] = None

        if self._keypoints_sink is not None:
            kp_writer = self._keypoints_sink.create_writer(frame_id)
            kp_ctx = KeyPointsDataContext(frame_id, kp_writer)

        if self._segmentation_frame_sink is not None:
            seg_writer = SegmentationResultWriter(
                frame_id, width, height, frame_sink=self._segmentation_frame_sink
            )
            seg_ctx = SegmentationDataContext(frame_id, seg_writer)

        return kp_ctx, seg_ctx

    def _create_frame_sink(self, protocol: Any, address: str) -> IFrameSink:
        """Create frame sink from protocol using FrameSinkFactory."""
        return FrameSinkFactory.create(protocol, address, logger_instance=logger)

    def close(self) -> None:
        """Release resources."""
        if self._closed:
            return

        logger.info("Closing RocketWelderClient")

        # Close frame sinks (KeyPointsSink has owns_sink=False, so we manage lifecycle)
        self._keypoints_sink = None
        if self._keypoints_frame_sink is not None:
            logger.debug("Closing keypoints frame sink")
            self._keypoints_frame_sink.close()
            self._keypoints_frame_sink = None

        if self._segmentation_frame_sink is not None:
            logger.debug("Closing segmentation frame sink")
            self._segmentation_frame_sink.close()
            self._segmentation_frame_sink = None

        self._closed = True
        logger.info("RocketWelderClient closed")

    def __enter__(self) -> RocketWelderClient:
        return self

    def __exit__(self, *args: object) -> None:
        self.close()
