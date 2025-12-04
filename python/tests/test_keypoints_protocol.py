"""Unit tests for keypoints protocol."""

import io
import json

import pytest

from rocket_welder_sdk.keypoints_protocol import (
    KeyPoint,
    KeyPointsSink,
    _confidence_from_ushort,
    _confidence_to_ushort,
    _read_varint,
    _write_varint,
    _zigzag_decode,
    _zigzag_encode,
)


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
        assert _confidence_to_ushort(0.0) == 0
        assert _confidence_to_ushort(1.0) == 10000
        assert _confidence_to_ushort(0.5) == 5000
        assert _confidence_to_ushort(0.9999) == 9999

    def test_confidence_from_ushort(self) -> None:
        """Test confidence ushort to float conversion."""
        assert _confidence_from_ushort(0) == 0.0
        assert _confidence_from_ushort(10000) == 1.0
        assert _confidence_from_ushort(5000) == 0.5

    def test_confidence_roundtrip(self) -> None:
        """Test confidence conversion roundtrip."""
        for value in [0.0, 0.25, 0.5, 0.75, 1.0]:
            ushort = _confidence_to_ushort(value)
            recovered = _confidence_from_ushort(ushort)
            assert abs(recovered - value) < 0.0001


class TestKeyPoint:
    """Tests for KeyPoint dataclass."""

    def test_keypoint_valid(self) -> None:
        """Test valid keypoint creation."""
        kp = KeyPoint(0, 100, 200, 0.95)
        assert kp.keypoint_id == 0
        assert kp.x == 100
        assert kp.y == 200
        assert kp.confidence == 0.95

    def test_keypoint_invalid_confidence_raises(self) -> None:
        """Test that invalid confidence raises ValueError."""
        with pytest.raises(ValueError, match="Confidence"):
            KeyPoint(0, 100, 200, 1.5)

        with pytest.raises(ValueError, match="Confidence"):
            KeyPoint(0, 100, 200, -0.1)


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

        # Check trajectory points
        assert trajectory[0] == (0, (100, 200), 0.95)
        assert trajectory[1] == (1, (110, 205), 0.95)
        assert trajectory[2] == (2, (120, 210), 0.95)

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
