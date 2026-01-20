"""
Unit tests for DeltaFrame class.
Matches C# DeltaFrame<T> struct behavior from RocketWelder.SDK.Protocols.
"""

from dataclasses import dataclass

import pytest

from rocket_welder_sdk.delta_frame import DeltaFrame


@dataclass(frozen=True, slots=True)
class MockKeyPoint:
    """Mock KeyPoint for testing DeltaFrame."""

    x: int
    y: int


class TestDeltaFrame:
    """Test suite for DeltaFrame class."""

    def test_create_master_frame(self) -> None:
        """Test creating a master frame (is_delta=False)."""
        items = [MockKeyPoint(100, 200), MockKeyPoint(150, 250)]
        frame = DeltaFrame[MockKeyPoint](frame_id=1, is_delta=False, items=items)

        assert frame.frame_id == 1
        assert frame.is_delta is False
        assert frame.is_master is True
        assert len(frame.items) == 2

    def test_create_delta_frame(self) -> None:
        """Test creating a delta frame (is_delta=True)."""
        items = [MockKeyPoint(5, -3), MockKeyPoint(-2, 1)]
        frame = DeltaFrame[MockKeyPoint](frame_id=2, is_delta=True, items=items)

        assert frame.frame_id == 2
        assert frame.is_delta is True
        assert frame.is_master is False
        assert len(frame.items) == 2

    def test_master_factory_method(self) -> None:
        """Test DeltaFrame.master() factory method."""
        items = [MockKeyPoint(100, 200)]
        frame = DeltaFrame.master(frame_id=1, items=items)

        assert frame.frame_id == 1
        assert frame.is_delta is False
        assert frame.is_master is True

    def test_delta_factory_method(self) -> None:
        """Test DeltaFrame.delta() factory method."""
        items = [MockKeyPoint(5, -3)]
        frame = DeltaFrame.delta(frame_id=2, items=items)

        assert frame.frame_id == 2
        assert frame.is_delta is True
        assert frame.is_master is False

    def test_empty_master_factory(self) -> None:
        """Test DeltaFrame.empty_master() factory method."""
        frame: DeltaFrame[MockKeyPoint] = DeltaFrame.empty_master(frame_id=1)

        assert frame.frame_id == 1
        assert frame.is_delta is False
        assert len(frame.items) == 0
        assert frame.count == 0

    def test_empty_delta_factory(self) -> None:
        """Test DeltaFrame.empty_delta() factory method."""
        frame: DeltaFrame[MockKeyPoint] = DeltaFrame.empty_delta(frame_id=2)

        assert frame.frame_id == 2
        assert frame.is_delta is True
        assert len(frame.items) == 0
        assert frame.count == 0

    def test_frame_id_validation_negative(self) -> None:
        """Test that negative frame_id raises ValueError."""
        with pytest.raises(ValueError):
            DeltaFrame[MockKeyPoint](frame_id=-1, is_delta=False, items=[])

    def test_frame_id_zero(self) -> None:
        """Test that zero frame_id is valid."""
        frame = DeltaFrame[MockKeyPoint](frame_id=0, is_delta=False, items=[])
        assert frame.frame_id == 0

    def test_frame_id_large(self) -> None:
        """Test that large frame_id values are valid (ulong equivalent)."""
        large_id = 2**63  # Large value within ulong range
        frame = DeltaFrame[MockKeyPoint](frame_id=large_id, is_delta=False, items=[])
        assert frame.frame_id == large_id

    def test_count_property(self) -> None:
        """Test count property returns number of items."""
        items = [MockKeyPoint(1, 2), MockKeyPoint(3, 4), MockKeyPoint(5, 6)]
        frame = DeltaFrame[MockKeyPoint](frame_id=1, is_delta=False, items=items)

        assert frame.count == 3

    def test_len_dunder(self) -> None:
        """Test __len__ returns number of items."""
        items = [MockKeyPoint(1, 2), MockKeyPoint(3, 4)]
        frame = DeltaFrame[MockKeyPoint](frame_id=1, is_delta=False, items=items)

        assert len(frame) == 2

    def test_bool_with_items(self) -> None:
        """Test __bool__ returns True when frame has items."""
        items = [MockKeyPoint(1, 2)]
        frame = DeltaFrame[MockKeyPoint](frame_id=1, is_delta=False, items=items)

        assert bool(frame) is True

    def test_bool_empty(self) -> None:
        """Test __bool__ returns False when frame is empty."""
        frame: DeltaFrame[MockKeyPoint] = DeltaFrame.empty_master(frame_id=1)

        assert bool(frame) is False

    def test_iter_items(self) -> None:
        """Test iterating over frame items."""
        items = [MockKeyPoint(1, 2), MockKeyPoint(3, 4)]
        frame = DeltaFrame[MockKeyPoint](frame_id=1, is_delta=False, items=items)

        iterated = list(frame)
        assert len(iterated) == 2
        assert iterated[0] == MockKeyPoint(1, 2)
        assert iterated[1] == MockKeyPoint(3, 4)

    def test_getitem_index(self) -> None:
        """Test getting item by index."""
        items = [MockKeyPoint(1, 2), MockKeyPoint(3, 4)]
        frame = DeltaFrame[MockKeyPoint](frame_id=1, is_delta=False, items=items)

        assert frame[0] == MockKeyPoint(1, 2)
        assert frame[1] == MockKeyPoint(3, 4)

    def test_getitem_negative_index(self) -> None:
        """Test getting item by negative index."""
        items = [MockKeyPoint(1, 2), MockKeyPoint(3, 4)]
        frame = DeltaFrame[MockKeyPoint](frame_id=1, is_delta=False, items=items)

        assert frame[-1] == MockKeyPoint(3, 4)
        assert frame[-2] == MockKeyPoint(1, 2)

    def test_getitem_out_of_range(self) -> None:
        """Test getting item with out of range index raises IndexError."""
        items = [MockKeyPoint(1, 2)]
        frame = DeltaFrame[MockKeyPoint](frame_id=1, is_delta=False, items=items)

        with pytest.raises(IndexError):
            _ = frame[10]

    def test_frozen(self) -> None:
        """Test that DeltaFrame is immutable (frozen dataclass)."""
        items = [MockKeyPoint(1, 2)]
        frame = DeltaFrame[MockKeyPoint](frame_id=1, is_delta=False, items=items)

        with pytest.raises(AttributeError):
            frame.frame_id = 2  # type: ignore[misc]

        with pytest.raises(AttributeError):
            frame.is_delta = True  # type: ignore[misc]

    def test_equality(self) -> None:
        """Test equality comparison."""
        items1 = [MockKeyPoint(1, 2)]
        items2 = [MockKeyPoint(1, 2)]
        items3 = [MockKeyPoint(3, 4)]

        frame1 = DeltaFrame[MockKeyPoint](frame_id=1, is_delta=False, items=items1)
        frame2 = DeltaFrame[MockKeyPoint](frame_id=1, is_delta=False, items=items2)
        frame3 = DeltaFrame[MockKeyPoint](frame_id=1, is_delta=False, items=items3)
        frame4 = DeltaFrame[MockKeyPoint](frame_id=2, is_delta=False, items=items1)

        assert frame1 == frame2  # Same frame_id, is_delta, and items
        assert frame1 != frame3  # Different items
        assert frame1 != frame4  # Different frame_id

    def test_hash(self) -> None:
        """Test that DeltaFrame with tuple items is hashable."""
        # Note: DeltaFrame with list items won't be hashable, but with tuple it will
        items = (MockKeyPoint(1, 2), MockKeyPoint(3, 4))
        frame = DeltaFrame[MockKeyPoint](frame_id=1, is_delta=False, items=items)

        # Should be able to use in set/dict
        s = {frame}
        assert len(s) == 1

    def test_generic_with_different_types(self) -> None:
        """Test DeltaFrame works with different generic types."""

        @dataclass(frozen=True)
        class SegmentInstance:
            class_id: int
            confidence: float

        items = [SegmentInstance(1, 0.95), SegmentInstance(2, 0.87)]
        frame = DeltaFrame[SegmentInstance](frame_id=1, is_delta=False, items=items)

        assert len(frame) == 2
        assert frame[0].class_id == 1
        assert frame[1].confidence == 0.87

    def test_generic_with_primitive_types(self) -> None:
        """Test DeltaFrame works with primitive types."""
        items = [1, 2, 3, 4, 5]
        frame = DeltaFrame[int](frame_id=1, is_delta=False, items=items)

        assert len(frame) == 5
        assert frame[0] == 1
        assert frame[4] == 5
