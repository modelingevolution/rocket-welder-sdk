"""
Data context types for per-frame keypoints and segmentation data.

Implements the Unit of Work pattern - contexts are created per-frame
and auto-commit when the processing delegate returns.
"""

from __future__ import annotations

from abc import ABC, abstractmethod
from typing import TYPE_CHECKING, Sequence, Tuple, Union

import numpy as np
import numpy.typing as npt

if TYPE_CHECKING:
    from rocket_welder_sdk.keypoints_protocol import IKeyPointsWriter
    from rocket_welder_sdk.segmentation_result import SegmentationResultWriter

    from .schema import KeyPointDefinition, SegmentClass

# Type aliases
Point = Tuple[int, int]


class IKeyPointsDataContext(ABC):
    """
    Unit of Work for keypoints data, scoped to a single frame.

    Auto-commits when the processing delegate returns.
    """

    @property
    @abstractmethod
    def frame_id(self) -> int:
        """Current frame ID."""
        pass

    @abstractmethod
    def add(self, point: KeyPointDefinition, x: int, y: int, confidence: float) -> None:
        """
        Add a keypoint detection for this frame.

        Args:
            point: KeyPointDefinition from schema definition
            x: X coordinate in pixels
            y: Y coordinate in pixels
            confidence: Detection confidence (0.0 to 1.0)
        """
        pass

    @abstractmethod
    def add_point(self, point: KeyPointDefinition, position: Point, confidence: float) -> None:
        """
        Add a keypoint detection using a Point tuple.

        Args:
            point: KeyPointDefinition from schema definition
            position: (x, y) tuple
            confidence: Detection confidence (0.0 to 1.0)
        """
        pass

    @abstractmethod
    def commit(self) -> None:
        """Commit the context (called automatically when delegate returns)."""
        pass


class ISegmentationDataContext(ABC):
    """
    Unit of Work for segmentation data, scoped to a single frame.

    Auto-commits when the processing delegate returns.
    """

    @property
    @abstractmethod
    def frame_id(self) -> int:
        """Current frame ID."""
        pass

    @abstractmethod
    def add(
        self,
        segment_class: SegmentClass,
        instance_id: int,
        points: Union[Sequence[Point], npt.NDArray[np.int32]],
    ) -> None:
        """
        Add a segmentation instance for this frame.

        Args:
            segment_class: SegmentClass from schema definition
            instance_id: Instance ID (for multiple instances of same class, 0-255)
            points: Contour points defining the instance boundary
        """
        pass

    @abstractmethod
    def commit(self) -> None:
        """Commit the context (called automatically when delegate returns)."""
        pass


class KeyPointsDataContext(IKeyPointsDataContext):
    """Implementation of keypoints data context."""

    def __init__(
        self,
        frame_id: int,
        writer: IKeyPointsWriter,
    ) -> None:
        self._frame_id = frame_id
        self._writer = writer

    @property
    def frame_id(self) -> int:
        return self._frame_id

    def add(self, point: KeyPointDefinition, x: int, y: int, confidence: float) -> None:
        """Add a keypoint detection for this frame."""
        self._writer.append(point.id, x, y, confidence)

    def add_point(self, point: KeyPointDefinition, position: Point, confidence: float) -> None:
        """Add a keypoint detection using a Point tuple."""
        self._writer.append_point(point.id, position, confidence)

    def commit(self) -> None:
        """Commit the context (called automatically when delegate returns)."""
        self._writer.close()


class SegmentationDataContext(ISegmentationDataContext):
    """Implementation of segmentation data context."""

    def __init__(
        self,
        frame_id: int,
        writer: SegmentationResultWriter,
    ) -> None:
        self._frame_id = frame_id
        self._writer = writer

    @property
    def frame_id(self) -> int:
        return self._frame_id

    def add(
        self,
        segment_class: SegmentClass,
        instance_id: int,
        points: Union[Sequence[Point], npt.NDArray[np.int32]],
    ) -> None:
        """Add a segmentation instance for this frame."""
        if instance_id < 0 or instance_id > 255:
            raise ValueError(f"instance_id must be 0-255, got {instance_id}")

        # Convert to numpy array if needed
        if isinstance(points, np.ndarray):
            points_array = points
        else:
            points_array = np.array(points, dtype=np.int32)

        self._writer.append(segment_class.class_id, instance_id, points_array)

    def commit(self) -> None:
        """Commit the context (called automatically when delegate returns)."""
        self._writer.close()
