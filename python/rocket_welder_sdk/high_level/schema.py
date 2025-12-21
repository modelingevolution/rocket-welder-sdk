"""
Schema types for KeyPoints and Segmentation.

Provides type-safe definitions for keypoints and segmentation classes
that are defined at initialization time and used during processing.
"""

from __future__ import annotations

import json
from abc import ABC, abstractmethod
from dataclasses import dataclass
from typing import Any, Dict, List


@dataclass(frozen=True)
class KeyPointDefinition:
    """
    A keypoint definition with ID and name.

    Created via IKeyPointsSchema.define_point().
    Used as a type-safe handle when adding keypoints to data context.
    """

    id: int
    name: str

    def __str__(self) -> str:
        return f"KeyPointDefinition({self.id}, '{self.name}')"


@dataclass(frozen=True)
class SegmentClass:
    """
    A segmentation class definition with class ID and name.

    Created via ISegmentationSchema.define_class().
    Used as a type-safe handle when adding instances to data context.
    """

    class_id: int
    name: str

    def __str__(self) -> str:
        return f"SegmentClass({self.class_id}, '{self.name}')"


class IKeyPointsSchema(ABC):
    """
    Interface for defining keypoints schema.

    Keypoints are defined once at initialization and referenced by handle
    when adding data to the context.
    """

    @abstractmethod
    def define_point(self, name: str) -> KeyPointDefinition:
        """
        Define a new keypoint.

        Args:
            name: Human-readable name for the keypoint (e.g., "nose", "left_eye")

        Returns:
            KeyPointDefinition handle for use with IKeyPointsDataContext.add()
        """
        pass

    @property
    @abstractmethod
    def defined_points(self) -> List[KeyPointDefinition]:
        """Get all defined keypoints."""
        pass

    @abstractmethod
    def get_metadata_json(self) -> str:
        """Get JSON metadata for serialization."""
        pass


class ISegmentationSchema(ABC):
    """
    Interface for defining segmentation classes schema.

    Classes are defined once at initialization and referenced by handle
    when adding instances to the context.
    """

    @abstractmethod
    def define_class(self, class_id: int, name: str) -> SegmentClass:
        """
        Define a new segmentation class.

        Args:
            class_id: Unique class identifier (0-255)
            name: Human-readable name for the class (e.g., "person", "car")

        Returns:
            SegmentClass handle for use with ISegmentationDataContext.add()
        """
        pass

    @property
    @abstractmethod
    def defined_classes(self) -> List[SegmentClass]:
        """Get all defined classes."""
        pass

    @abstractmethod
    def get_metadata_json(self) -> str:
        """Get JSON metadata for serialization."""
        pass


class KeyPointsSchema(IKeyPointsSchema):
    """Implementation of keypoints schema."""

    def __init__(self) -> None:
        self._points: Dict[str, KeyPointDefinition] = {}
        self._next_id = 0

    def define_point(self, name: str) -> KeyPointDefinition:
        """Define a new keypoint."""
        if name in self._points:
            raise ValueError(f"Keypoint '{name}' already defined")

        point = KeyPointDefinition(id=self._next_id, name=name)
        self._points[name] = point
        self._next_id += 1
        return point

    @property
    def defined_points(self) -> List[KeyPointDefinition]:
        """Get all defined keypoints."""
        return list(self._points.values())

    def get_metadata_json(self) -> str:
        """
        Get JSON metadata for serialization.

        Format matches C# SDK:
        {
            "version": 1,
            "type": "keypoints",
            "points": [{"id": 0, "name": "nose"}, ...]
        }
        """
        metadata: Dict[str, Any] = {
            "version": 1,
            "type": "keypoints",
            "points": [{"id": p.id, "name": p.name} for p in self._points.values()],
        }
        return json.dumps(metadata, indent=2)


class SegmentationSchema(ISegmentationSchema):
    """Implementation of segmentation schema."""

    def __init__(self) -> None:
        self._classes: Dict[int, SegmentClass] = {}

    def define_class(self, class_id: int, name: str) -> SegmentClass:
        """Define a new segmentation class."""
        if class_id < 0 or class_id > 255:
            raise ValueError(f"class_id must be 0-255, got {class_id}")

        if class_id in self._classes:
            raise ValueError(f"Class ID {class_id} already defined")

        segment_class = SegmentClass(class_id=class_id, name=name)
        self._classes[class_id] = segment_class
        return segment_class

    @property
    def defined_classes(self) -> List[SegmentClass]:
        """Get all defined classes."""
        return list(self._classes.values())

    def get_metadata_json(self) -> str:
        """
        Get JSON metadata for serialization.

        Format matches C# SDK:
        {
            "version": 1,
            "type": "segmentation",
            "classes": [{"classId": 1, "name": "person"}, ...]
        }
        """
        metadata: Dict[str, Any] = {
            "version": 1,
            "type": "segmentation",
            "classes": [{"classId": c.class_id, "name": c.name} for c in self._classes.values()],
        }
        return json.dumps(metadata, indent=2)
