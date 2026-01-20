"""
DeltaFrame generic container for delta-encoded streaming data.
Matches C# DeltaFrame<T> struct from RocketWelder.SDK.Protocols.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Generic, Iterator, Sequence, TypeVar

# TypeVar for the item type (equivalent to C# struct constraint)
T = TypeVar("T")


@dataclass(frozen=True)
class DeltaFrame(Generic[T]):
    """
    Generic container for delta-encoded streaming data.

    Used by Reader/Source classes where IsDelta is needed for decoding.
    When IsDelta is True, the items contain delta-encoded values relative
    to the previous frame, otherwise they contain absolute values.

    Type Parameters:
        T: The item type (e.g., KeyPoint, SegmentationInstance)

    Attributes:
        frame_id: The frame identifier (ulong equivalent).
        is_delta: True if this frame contains delta-encoded values relative to previous frame.
        items: The items in this frame. Caller owns the backing memory.

    Examples:
        >>> from dataclasses import dataclass
        >>> @dataclass(frozen=True)
        ... class KeyPoint:
        ...     x: int
        ...     y: int
        ...
        >>> # Create a master frame (absolute values)
        >>> master = DeltaFrame[KeyPoint](
        ...     frame_id=1,
        ...     is_delta=False,
        ...     items=[KeyPoint(100, 200), KeyPoint(150, 250)]
        ... )
        >>>
        >>> # Create a delta frame (relative values)
        >>> delta = DeltaFrame[KeyPoint](
        ...     frame_id=2,
        ...     is_delta=True,
        ...     items=[KeyPoint(5, -3), KeyPoint(-2, 1)]
        ... )
        >>>
        >>> # Check if it's a master frame
        >>> if not delta.is_delta:
        ...     print("Master frame")
    """

    frame_id: int
    """The frame identifier (ulong equivalent, 0 to 2^64-1)."""

    is_delta: bool
    """True if this frame contains delta-encoded values relative to previous frame."""

    items: Sequence[T]
    """The items in this frame. Caller owns the backing memory."""

    def __post_init__(self) -> None:
        """Validate frame_id is non-negative."""
        if self.frame_id < 0:
            raise ValueError(f"frame_id must be non-negative, got {self.frame_id}")

    @property
    def is_master(self) -> bool:
        """True if this is a master frame (not delta-encoded)."""
        return not self.is_delta

    @property
    def count(self) -> int:
        """Number of items in this frame."""
        return len(self.items)

    def __len__(self) -> int:
        """Return the number of items in this frame."""
        return len(self.items)

    def __bool__(self) -> bool:
        """Return True if the frame has items."""
        return len(self.items) > 0

    def __iter__(self) -> Iterator[T]:
        """Iterate over items in this frame."""
        return iter(self.items)

    def __getitem__(self, index: int) -> T:
        """Get item by index."""
        return self.items[index]

    @classmethod
    def master(cls, frame_id: int, items: Sequence[T]) -> DeltaFrame[T]:
        """
        Create a master frame (absolute values, not delta-encoded).

        Args:
            frame_id: The frame identifier
            items: The items with absolute values

        Returns:
            DeltaFrame with is_delta=False
        """
        return cls(frame_id=frame_id, is_delta=False, items=items)

    @classmethod
    def delta(cls, frame_id: int, items: Sequence[T]) -> DeltaFrame[T]:
        """
        Create a delta frame (values relative to previous frame).

        Args:
            frame_id: The frame identifier
            items: The items with delta-encoded values

        Returns:
            DeltaFrame with is_delta=True
        """
        return cls(frame_id=frame_id, is_delta=True, items=items)

    @classmethod
    def empty_master(cls, frame_id: int) -> DeltaFrame[T]:
        """
        Create an empty master frame.

        Args:
            frame_id: The frame identifier

        Returns:
            DeltaFrame with no items and is_delta=False
        """
        return cls(frame_id=frame_id, is_delta=False, items=[])

    @classmethod
    def empty_delta(cls, frame_id: int) -> DeltaFrame[T]:
        """
        Create an empty delta frame.

        Args:
            frame_id: The frame identifier

        Returns:
            DeltaFrame with no items and is_delta=True
        """
        return cls(frame_id=frame_id, is_delta=True, items=[])
