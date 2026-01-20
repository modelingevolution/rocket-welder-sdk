"""Unit tests for keypoints protocol."""

from __future__ import annotations

import io
import json
from typing import Dict, List

import pytest

from rocket_welder_sdk.confidence import Confidence
from rocket_welder_sdk.delta_frame import DeltaFrame  # noqa: TC001
from rocket_welder_sdk.keypoints_protocol import (
    CONFIDENCE_MAX,
    KeyPoint,
    KeyPointsFrame,
    KeyPointsProtocol,
    KeyPointsSink,
    KeyPointsSource,
    _confidence_from_ushort,
    _confidence_to_ushort,
    _read_varint,
    _write_varint,
    _zigzag_decode,
    _zigzag_encode,
)
from rocket_welder_sdk.transport import StreamFrameSource


class TestVarintEncoding:
    """Tests for varint encoding/decoding."""

    def test_write_read_varint_small_values(self) -> None:
        """Test varint with small values (< 128)."""
        for value in [0, 1, 127]:
            stream = io.BytesIO()
            _write_varint(stream, value)
            stream.seek(0)
            assert _read_varint(stream) == value

    def test_write_read_varint_large_values(self) -> None:
        """Test varint with large values."""
        for value in [128, 256, 16384, 2097152, 268435456]:
            stream = io.BytesIO()
            _write_varint(stream, value)
            stream.seek(0)
            assert _read_varint(stream) == value

    def test_write_varint_negative_raises(self) -> None:
        """Test that negative values raise ValueError."""
        stream = io.BytesIO()
        with pytest.raises(ValueError, match="non-negative"):
            _write_varint(stream, -1)


class TestZigZagEncoding:
    """Tests for ZigZag encoding/decoding."""

    def test_zigzag_encode_decode_positive(self) -> None:
        """Test ZigZag with positive values."""
        for value in [0, 1, 100, 1000]:
            encoded = _zigzag_encode(value)
            decoded = _zigzag_decode(encoded)
            assert decoded == value

    def test_zigzag_encode_decode_negative(self) -> None:
        """Test ZigZag with negative values."""
        for value in [-1, -100, -1000]:
            encoded = _zigzag_encode(value)
            decoded = _zigzag_decode(encoded)
            assert decoded == value


class TestConfidenceEncoding:
    """Tests for confidence float<->ushort conversion."""

    def test_confidence_to_ushort(self) -> None:
        """Test confidence float to ushort conversion."""
        # Uses 65535 (ushort.MaxValue) scale like C#
        assert _confidence_to_ushort(0.0) == 0
        assert _confidence_to_ushort(1.0) == CONFIDENCE_MAX  # 65535
        assert _confidence_to_ushort(0.5) == 32767  # ~0.5 * 65535
        assert _confidence_to_ushort(0.9999) == 65528  # ~0.9999 * 65535

    def test_confidence_from_ushort(self) -> None:
        """Test confidence ushort to float conversion."""
        assert _confidence_from_ushort(0) == 0.0
        assert _confidence_from_ushort(CONFIDENCE_MAX) == 1.0
        assert abs(_confidence_from_ushort(32767) - 0.5) < 0.0001

    def test_confidence_roundtrip(self) -> None:
        """Test confidence conversion roundtrip."""
        for value in [0.0, 0.25, 0.5, 0.75, 1.0]:
            ushort = _confidence_to_ushort(value)
            recovered = _confidence_from_ushort(ushort)
            assert abs(recovered - value) < 0.0001


class TestKeyPoint:
    """Tests for KeyPoint dataclass."""

    def test_keypoint_valid(self) -> None:
        """Test valid keypoint creation using create()."""
        kp = KeyPoint.create(0, 100, 200, 0.95)
        assert kp.id == 0
        assert kp.keypoint_id == 0  # Legacy alias
        assert kp.x == 100
        assert kp.y == 200
        assert kp.position == (100, 200)
        assert abs(kp.confidence.normalized - 0.95) < 0.0001

    def test_keypoint_with_confidence_object(self) -> None:
        """Test keypoint creation with Confidence object."""
        conf = Confidence.from_float(0.95)
        kp = KeyPoint(id=0, position=(100, 200), confidence=conf)
        assert kp.id == 0
        assert kp.position == (100, 200)
        assert kp.confidence == conf

    def test_keypoint_create_with_raw_confidence(self) -> None:
        """Test keypoint creation with raw ushort confidence."""
        kp = KeyPoint.create(0, 100, 200, 65535)  # raw ushort
        assert kp.confidence.raw == 65535
        assert kp.confidence.normalized == 1.0

    def test_keypoint_invalid_confidence_raises(self) -> None:
        """Test that invalid confidence raises ValueError."""
        # Creating Confidence with out of range raw value
        with pytest.raises(ValueError):
            Confidence(raw=70000)  # > 65535


class TestKeyPointsWriter:
    """Tests for KeyPointsWriter."""

    def test_single_frame_roundtrip(self) -> None:
        """Test writing and reading a single master frame."""
        stream = io.BytesIO()
        storage = KeyPointsSink(stream)

        # Write
        with storage.create_writer(frame_id=1) as writer:
            writer.append(0, 100, 200, 0.95)
            writer.append(1, 120, 190, 0.92)
            writer.append(2, 80, 190, 0.88)

        # Read
        stream.seek(0)
        json_def = json.dumps(
            {
                "version": "1.0",
                "compute_module_name": "TestModel",
                "points": {"nose": 0, "left_eye": 1, "right_eye": 2},
            }
        )
        series = storage.read(json_def, stream)

        # Verify
        assert series.version == "1.0"
        assert series.compute_module_name == "TestModel"
        assert len(series.points) == 3
        assert series.contains_frame(1)

        frame = series.get_frame(1)
        assert frame is not None
        assert len(frame) == 3

        # Check keypoint 0
        point, conf = frame[0]
        assert point == (100, 200)
        assert abs(conf - 0.95) < 0.0001

    def test_multiple_frames_master_delta(self) -> None:
        """Test writing and reading multiple frames with delta encoding."""
        stream = io.BytesIO()
        storage = KeyPointsSink(stream, master_frame_interval=2)

        # Frame 0 - Master
        with storage.create_writer(frame_id=0) as writer:
            writer.append(0, 100, 200, 0.95)
            writer.append(1, 120, 190, 0.92)

        # Frame 1 - Delta
        with storage.create_writer(frame_id=1) as writer:
            writer.append(0, 101, 201, 0.94)
            writer.append(1, 121, 191, 0.93)

        # Frame 2 - Master (interval hit)
        with storage.create_writer(frame_id=2) as writer:
            writer.append(0, 105, 205, 0.96)
            writer.append(1, 125, 195, 0.91)

        # Read
        stream.seek(0)
        json_def = json.dumps(
            {
                "version": "1.0",
                "compute_module_name": "TestModel",
                "points": {"nose": 0, "left_eye": 1},
            }
        )
        series = storage.read(json_def, stream)

        # Verify
        assert len(series.frame_ids) == 3
        assert series.contains_frame(0)
        assert series.contains_frame(1)
        assert series.contains_frame(2)

        # Check frame 1 (delta decoded correctly)
        frame1 = series.get_frame(1)
        assert frame1 is not None
        point, conf = frame1[0]
        assert point == (101, 201)
        assert abs(conf - 0.94) < 0.0001


class TestKeyPointsSeries:
    """Tests for KeyPointsSeries."""

    def test_get_keypoint_trajectory(self) -> None:
        """Test getting keypoint trajectory across frames."""
        stream = io.BytesIO()
        storage = KeyPointsSink(stream)

        # Write 3 frames with nose moving
        for frame_id in range(3):
            with storage.create_writer(frame_id=frame_id) as writer:
                writer.append(0, 100 + frame_id * 10, 200 + frame_id * 5, 0.95)
                writer.append(1, 150, 250, 0.90)  # Static point

        # Read
        stream.seek(0)
        json_def = json.dumps(
            {
                "version": "1.0",
                "compute_module_name": "TestModel",
                "points": {"nose": 0, "left_eye": 1},
            }
        )
        series = storage.read(json_def, stream)

        # Get trajectory
        trajectory = list(series.get_keypoint_trajectory(0))
        assert len(trajectory) == 3

        # Check trajectory points (allow for ushort precision loss)
        assert trajectory[0][0] == 0  # frame_id
        assert trajectory[0][1] == (100, 200)  # position
        assert abs(trajectory[0][2] - 0.95) < 0.0001  # confidence

        assert trajectory[1][0] == 1
        assert trajectory[1][1] == (110, 205)
        assert abs(trajectory[1][2] - 0.95) < 0.0001

        assert trajectory[2][0] == 2
        assert trajectory[2][1] == (120, 210)
        assert abs(trajectory[2][2] - 0.95) < 0.0001

    def test_get_keypoint_trajectory_by_name(self) -> None:
        """Test getting keypoint trajectory by name."""
        stream = io.BytesIO()
        storage = KeyPointsSink(stream)

        # Write 2 frames
        for frame_id in range(2):
            with storage.create_writer(frame_id=frame_id) as writer:
                writer.append(0, 100 + frame_id * 10, 200, 0.95)

        # Read
        stream.seek(0)
        json_def = json.dumps(
            {
                "version": "1.0",
                "compute_module_name": "TestModel",
                "points": {"nose": 0},
            }
        )
        series = storage.read(json_def, stream)

        # Get trajectory by name
        trajectory = list(series.get_keypoint_trajectory_by_name("nose"))
        assert len(trajectory) == 2
        assert trajectory[0][1] == (100, 200)
        assert trajectory[1][1] == (110, 200)

    def test_get_keypoint_by_name(self) -> None:
        """Test getting keypoint by name at specific frame."""
        stream = io.BytesIO()
        storage = KeyPointsSink(stream)

        with storage.create_writer(frame_id=10) as writer:
            writer.append(0, 100, 200, 0.95)
            writer.append(1, 120, 190, 0.92)

        # Read
        stream.seek(0)
        json_def = json.dumps(
            {
                "version": "1.0",
                "compute_module_name": "TestModel",
                "points": {"nose": 0, "left_eye": 1},
            }
        )
        series = storage.read(json_def, stream)

        # Get by name
        result = series.get_keypoint_by_name(10, "nose")
        assert result is not None
        point, conf = result
        assert point == (100, 200)
        assert abs(conf - 0.95) < 0.0001

        # Non-existent
        assert series.get_keypoint_by_name(999, "nose") is None

    def test_variable_keypoint_count(self) -> None:
        """Test frames with different keypoint counts."""
        stream = io.BytesIO()
        storage = KeyPointsSink(stream)

        # Frame 0 - 2 keypoints
        with storage.create_writer(frame_id=0) as writer:
            writer.append(0, 100, 200, 0.95)
            writer.append(1, 120, 190, 0.92)

        # Frame 1 - 4 keypoints (2 new appeared)
        with storage.create_writer(frame_id=1) as writer:
            writer.append(0, 101, 201, 0.94)
            writer.append(1, 121, 191, 0.93)
            writer.append(3, 150, 300, 0.88)
            writer.append(4, 50, 300, 0.85)

        # Frame 2 - 1 keypoint (most disappeared)
        with storage.create_writer(frame_id=2) as writer:
            writer.append(0, 102, 202, 0.96)

        # Read
        stream.seek(0)
        json_def = json.dumps(
            {
                "version": "1.0",
                "compute_module_name": "TestModel",
                "points": {"nose": 0, "left_eye": 1, "left_shoulder": 3, "right_shoulder": 4},
            }
        )
        series = storage.read(json_def, stream)

        # Verify
        assert len(series.get_frame(0)) == 2
        assert len(series.get_frame(1)) == 4
        assert len(series.get_frame(2)) == 1

        # Verify trajectory includes only frames where keypoint exists
        trajectory = list(series.get_keypoint_trajectory(3))
        assert len(trajectory) == 1
        assert trajectory[0][0] == 1  # frame_id

    def test_large_coordinates(self) -> None:
        """Test handling of large and negative coordinates."""
        stream = io.BytesIO()
        storage = KeyPointsSink(stream)

        with storage.create_writer(frame_id=1) as writer:
            writer.append(0, 0, 0, 1.0)
            writer.append(1, -1000, -2000, 0.9)
            writer.append(2, 1000000, 2000000, 0.8)
            writer.append(3, -1000000, -2000000, 0.7)

        # Read
        stream.seek(0)
        json_def = json.dumps(
            {
                "version": "1.0",
                "compute_module_name": "TestModel",
                "points": {},
            }
        )
        series = storage.read(json_def, stream)

        frame = series.get_frame(1)
        assert frame is not None

        assert frame[0][0] == (0, 0)
        assert frame[1][0] == (-1000, -2000)
        assert frame[2][0] == (1000000, 2000000)
        assert frame[3][0] == (-1000000, -2000000)


class TestKeyPointsFrame:
    """Tests for KeyPointsFrame dataclass."""

    def test_keypoints_frame_creation(self) -> None:
        """Test creating a KeyPointsFrame."""
        kp1 = KeyPoint.create(0, 100, 200, 0.95)
        kp2 = KeyPoint.create(1, 150, 250, 0.92)
        frame = KeyPointsFrame(frame_id=1, keypoints=[kp1, kp2])

        assert frame.frame_id == 1
        assert frame.count == 2
        assert len(frame) == 2

    def test_keypoints_frame_iteration(self) -> None:
        """Test iterating over KeyPointsFrame."""
        kp1 = KeyPoint.create(0, 100, 200, 0.95)
        kp2 = KeyPoint.create(1, 150, 250, 0.92)
        frame = KeyPointsFrame(frame_id=1, keypoints=[kp1, kp2])

        keypoints = list(frame)
        assert len(keypoints) == 2
        assert keypoints[0] == kp1
        assert keypoints[1] == kp2

    def test_keypoints_frame_indexing(self) -> None:
        """Test indexing KeyPointsFrame."""
        kp1 = KeyPoint.create(0, 100, 200, 0.95)
        kp2 = KeyPoint.create(1, 150, 250, 0.92)
        frame = KeyPointsFrame(frame_id=1, keypoints=[kp1, kp2])

        assert frame[0] == kp1
        assert frame[1] == kp2

    def test_keypoints_frame_find_by_id(self) -> None:
        """Test finding keypoint by ID."""
        kp1 = KeyPoint.create(0, 100, 200, 0.95)
        kp2 = KeyPoint.create(5, 150, 250, 0.92)
        frame = KeyPointsFrame(frame_id=1, keypoints=[kp1, kp2])

        found = frame.find_by_id(5)
        assert found is not None
        assert found.id == 5

        assert frame.find_by_id(99) is None


class TestKeyPointsProtocol:
    """Tests for KeyPointsProtocol static methods."""

    def test_write_and_read_master_frame(self) -> None:
        """Test writing and reading a master frame."""
        keypoints = [
            KeyPoint.create(0, 100, 200, 0.95),
            KeyPoint.create(1, 150, 250, 0.92),
        ]

        data = KeyPointsProtocol.write_master_frame(frame_id=1, keypoints=keypoints)
        frame = KeyPointsProtocol.read(data)

        assert frame.frame_id == 1
        assert not frame.is_delta
        assert frame.count == 2

        kp0 = frame[0]
        assert kp0.id == 0
        assert kp0.position == (100, 200)
        assert abs(kp0.confidence.normalized - 0.95) < 0.0001

    def test_write_and_read_delta_frame(self) -> None:
        """Test writing and reading a delta frame with previous state."""
        prev_keypoints = [
            KeyPoint.create(0, 100, 200, 0.95),
            KeyPoint.create(1, 150, 250, 0.92),
        ]
        prev_lookup: Dict[int, KeyPoint] = {kp.id: kp for kp in prev_keypoints}

        curr_keypoints = [
            KeyPoint.create(0, 101, 201, 0.94),
            KeyPoint.create(1, 151, 251, 0.93),
        ]

        data = KeyPointsProtocol.write_delta_frame(
            frame_id=2,
            current=curr_keypoints,
            previous_lookup=prev_lookup,
        )

        frame = KeyPointsProtocol.read_with_previous_state(data, prev_lookup)

        assert frame.frame_id == 2
        assert frame.is_delta
        assert frame.count == 2

        kp0 = frame[0]
        assert kp0.id == 0
        assert kp0.position == (101, 201)
        assert abs(kp0.confidence.normalized - 0.94) < 0.001

    def test_is_master_frame(self) -> None:
        """Test detecting master vs delta frame."""
        master_data = KeyPointsProtocol.write_master_frame(
            frame_id=1, keypoints=[KeyPoint.create(0, 100, 200, 0.95)]
        )
        assert KeyPointsProtocol.is_master_frame(master_data) is True

        prev_lookup = {0: KeyPoint.create(0, 100, 200, 0.95)}
        delta_data = KeyPointsProtocol.write_delta_frame(
            frame_id=2,
            current=[KeyPoint.create(0, 101, 201, 0.94)],
            previous_lookup=prev_lookup,
        )
        assert KeyPointsProtocol.is_master_frame(delta_data) is False

    def test_should_write_master_frame(self) -> None:
        """Test master frame interval logic."""
        # First frame is always master
        assert KeyPointsProtocol.should_write_master_frame(0, master_interval=300) is True

        # Frame at interval is master
        assert KeyPointsProtocol.should_write_master_frame(300, master_interval=300) is True
        assert KeyPointsProtocol.should_write_master_frame(600, master_interval=300) is True

        # Other frames are delta
        assert KeyPointsProtocol.should_write_master_frame(1, master_interval=300) is False
        assert KeyPointsProtocol.should_write_master_frame(299, master_interval=300) is False

    def test_calculate_frame_sizes(self) -> None:
        """Test frame size calculations."""
        # Master frame: type(1) + frameId(8) + count(5) + keypoints(15 each)
        assert KeyPointsProtocol.calculate_master_frame_size(0) == 14
        assert KeyPointsProtocol.calculate_master_frame_size(2) == 14 + 30

        # Delta frame: type(1) + frameId(8) + count(5) + keypoints(20 each)
        assert KeyPointsProtocol.calculate_delta_frame_size(0) == 14
        assert KeyPointsProtocol.calculate_delta_frame_size(2) == 14 + 40


class TestKeyPointsSource:
    """Tests for KeyPointsSource streaming reader."""

    def test_source_reads_frames(self) -> None:
        """Test reading frames through KeyPointsSource."""
        # Write frames to stream
        stream = io.BytesIO()
        storage = KeyPointsSink(stream)

        for frame_id in range(3):
            with storage.create_writer(frame_id=frame_id) as writer:
                writer.append(0, 100 + frame_id * 10, 200, 0.95)

        # Read using KeyPointsSource
        stream.seek(0)
        frame_source = StreamFrameSource(stream, leave_open=True)
        source = KeyPointsSource(frame_source)

        frames: List[DeltaFrame[KeyPoint]] = list(source.read_frames())

        assert len(frames) == 3
        assert frames[0].frame_id == 0
        assert frames[1].frame_id == 1
        assert frames[2].frame_id == 2

        # First frame is master
        assert not frames[0].is_delta
        # Subsequent frames are delta
        assert frames[1].is_delta
        assert frames[2].is_delta

        # Verify decoded values
        assert frames[0][0].x == 100
        assert frames[1][0].x == 110
        assert frames[2][0].x == 120

    def test_source_handles_variable_keypoints(self) -> None:
        """Test KeyPointsSource with variable keypoint counts."""
        stream = io.BytesIO()
        storage = KeyPointsSink(stream)

        # Frame 0: 2 keypoints
        with storage.create_writer(frame_id=0) as writer:
            writer.append(0, 100, 200, 0.95)
            writer.append(1, 150, 250, 0.92)

        # Frame 1: 3 keypoints (one new)
        with storage.create_writer(frame_id=1) as writer:
            writer.append(0, 101, 201, 0.94)
            writer.append(1, 151, 251, 0.93)
            writer.append(2, 200, 300, 0.88)

        # Read
        stream.seek(0)
        frame_source = StreamFrameSource(stream, leave_open=True)
        source = KeyPointsSource(frame_source)

        frames = list(source.read_frames())

        assert frames[0].count == 2
        assert frames[1].count == 3

        # New keypoint in frame 1
        kp2 = next(kp for kp in frames[1] if kp.id == 2)
        assert kp2.position == (200, 300)
