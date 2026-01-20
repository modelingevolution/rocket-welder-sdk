"""Unit tests for segmentation result serialization."""

import io
import struct
from typing import List, Tuple

import numpy as np
import pytest

from rocket_welder_sdk.segmentation_result import (
    SegmentationFrame,
    SegmentationInstance,
    SegmentationProtocol,
    SegmentationResultReader,
    SegmentationResultSource,
    SegmentationResultWriter,
)
from rocket_welder_sdk.transport import StreamFrameSink, StreamFrameSource


def _read_frame_via_transport(stream: io.BytesIO) -> SegmentationResultReader:
    """Helper to read a single frame via transport layer."""
    stream.seek(0)
    frame_source = StreamFrameSource(stream)
    frame_data = frame_source.read_frame()
    if frame_data is None:
        raise ValueError("No frame data found")
    return SegmentationResultReader(io.BytesIO(frame_data))


class TestRoundTrip:
    """Round-trip tests: write then read."""

    def test_single_instance_preserves_data(self) -> None:
        """Test that single instance round-trips correctly."""
        # Arrange
        frame_id = 42
        width = 1920
        height = 1080
        class_id = 5
        instance_id = 1
        points = np.array([[100, 200], [101, 201], [102, 199], [105, 200]], dtype=np.int32)

        stream = io.BytesIO()

        # Act - Write
        with SegmentationResultWriter(frame_id, width, height, stream) as writer:
            writer.append(class_id, instance_id, points)

        # Act - Read via transport layer
        with _read_frame_via_transport(stream) as reader:
            metadata = reader.metadata
            assert metadata.frame_id == frame_id
            assert metadata.width == width
            assert metadata.height == height

            instance = reader.read_next()
            assert instance is not None
            assert instance.class_id == class_id
            assert instance.instance_id == instance_id
            assert len(instance.points) == len(points)
            np.testing.assert_array_equal(instance.points, points)

            # Should be end of frame
            assert reader.read_next() is None

    def test_multiple_instances_preserves_data(self) -> None:
        """Test that multiple instances round-trip correctly."""
        # Arrange
        frame_id = 100
        width = 640
        height = 480

        instances = [
            (1, 1, np.array([[10, 20], [30, 40]], dtype=np.int32)),
            (2, 1, np.array([[100, 100], [101, 101], [102, 100]], dtype=np.int32)),
            (1, 2, np.array([[500, 400]], dtype=np.int32)),
        ]

        stream = io.BytesIO()

        # Act - Write
        with SegmentationResultWriter(frame_id, width, height, stream) as writer:
            for class_id, instance_id, points in instances:
                writer.append(class_id, instance_id, points)

        # Act - Read via transport layer
        # Via transport layer
        with _read_frame_via_transport(stream) as reader:
            metadata = reader.metadata
            assert metadata.frame_id == frame_id

            for i, (expected_class, expected_inst, expected_points) in enumerate(instances):
                instance = reader.read_next()
                assert instance is not None, f"Instance {i} should exist"
                assert instance.class_id == expected_class
                assert instance.instance_id == expected_inst
                np.testing.assert_array_equal(instance.points, expected_points)

            assert reader.read_next() is None

    def test_empty_points_preserves_data(self) -> None:
        """Test that empty points array works."""
        stream = io.BytesIO()

        with SegmentationResultWriter(1, 100, 100, stream) as writer:
            writer.append(1, 1, np.empty((0, 2), dtype=np.int32))

        # Via transport layer
        with _read_frame_via_transport(stream) as reader:
            instance = reader.read_next()
            assert instance is not None
            assert instance.class_id == 1
            assert instance.instance_id == 1
            assert len(instance.points) == 0

    def test_large_contour_preserves_data(self) -> None:
        """Test that large contour (1000 points) works."""
        # Create circle contour
        angles = np.linspace(0, 2 * np.pi, 1000, endpoint=False)
        points = np.column_stack(
            (
                (1920 + 500 * np.cos(angles)).astype(np.int32),
                (1080 + 500 * np.sin(angles)).astype(np.int32),
            )
        )

        stream = io.BytesIO()

        with SegmentationResultWriter(999, 3840, 2160, stream) as writer:
            writer.append(10, 5, points)

        # Via transport layer
        with _read_frame_via_transport(stream) as reader:
            instance = reader.read_next()
            assert instance is not None
            assert len(instance.points) == 1000
            np.testing.assert_array_equal(instance.points, points)

    def test_negative_deltas_preserves_data(self) -> None:
        """Test that negative deltas work correctly."""
        points = np.array(
            [
                [100, 100],
                [99, 99],  # -1, -1
                [98, 100],  # -1, +1
                [100, 98],  # +2, -2
                [50, 150],  # -50, +52
            ],
            dtype=np.int32,
        )

        stream = io.BytesIO()

        with SegmentationResultWriter(1, 200, 200, stream) as writer:
            writer.append(1, 1, points)

        # Via transport layer
        with _read_frame_via_transport(stream) as reader:
            instance = reader.read_next()
            assert instance is not None
            np.testing.assert_array_equal(instance.points, points)

    def test_multiple_frames_in_one_stream(self) -> None:
        """Test that multiple frames can be written and read via transport layer."""
        from rocket_welder_sdk.transport import StreamFrameSink, StreamFrameSource

        stream = io.BytesIO()

        # Frame 1
        frame1_points = [(1, 1, np.array([[10, 20], [30, 40]], dtype=np.int32))]

        with SegmentationResultWriter(
            1, 640, 480, frame_sink=StreamFrameSink(stream, leave_open=True)
        ) as writer:
            for class_id, instance_id, points in frame1_points:
                writer.append(class_id, instance_id, points)

        # Frame 2
        frame2_points = [
            (2, 1, np.array([[100, 200]], dtype=np.int32)),
            (3, 1, np.array([[500, 600], [510, 610], [520, 620]], dtype=np.int32)),
        ]

        with SegmentationResultWriter(
            2, 1920, 1080, frame_sink=StreamFrameSink(stream, leave_open=True)
        ) as writer:
            for class_id, instance_id, points in frame2_points:
                writer.append(class_id, instance_id, points)

        # Read both frames via transport layer
        stream.seek(0)
        frame_source = StreamFrameSource(stream)

        # Read frame 1
        frame1_data = frame_source.read_frame()
        assert frame1_data is not None and len(frame1_data) > 0
        with SegmentationResultReader(io.BytesIO(frame1_data)) as reader1:
            metadata1 = reader1.metadata
            assert metadata1.frame_id == 1
            assert metadata1.width == 640
            assert metadata1.height == 480

            for expected_class, expected_inst, expected_points in frame1_points:
                instance = reader1.read_next()
                assert instance is not None
                assert instance.class_id == expected_class
                assert instance.instance_id == expected_inst
                np.testing.assert_array_equal(instance.points, expected_points)

            assert reader1.read_next() is None

        # Read frame 2
        frame2_data = frame_source.read_frame()
        assert len(frame2_data) > 0
        with SegmentationResultReader(io.BytesIO(frame2_data)) as reader2:
            metadata2 = reader2.metadata
            assert metadata2.frame_id == 2
            assert metadata2.width == 1920
            assert metadata2.height == 1080

            for expected_class, expected_inst, expected_points in frame2_points:
                instance = reader2.read_next()
                assert instance is not None
                assert instance.class_id == expected_class
                assert instance.instance_id == expected_inst
                np.testing.assert_array_equal(instance.points, expected_points)

            assert reader2.read_next() is None


class TestNormalization:
    """Tests for coordinate normalization."""

    def test_to_normalized_converts_to_float_range(self) -> None:
        """Test normalization to [0-1] range."""
        points = np.array([[0, 0], [1920, 1080], [960, 540]], dtype=np.int32)
        instance = SegmentationInstance(1, 1, points)

        normalized = instance.to_normalized(1920, 1080)

        assert normalized.dtype == np.float32
        np.testing.assert_array_almost_equal(normalized[0], [0.0, 0.0], decimal=5)
        np.testing.assert_array_almost_equal(normalized[1], [1.0, 1.0], decimal=5)
        np.testing.assert_array_almost_equal(normalized[2], [0.5, 0.5], decimal=5)

    def test_to_normalized_raises_on_zero_dimensions(self) -> None:
        """Test that normalization raises on zero width/height."""
        points = np.array([[10, 20]], dtype=np.int32)
        instance = SegmentationInstance(1, 1, points)

        with pytest.raises(ValueError, match="must be positive"):
            instance.to_normalized(0, 1080)

        with pytest.raises(ValueError, match="must be positive"):
            instance.to_normalized(1920, 0)


class TestIterator:
    """Tests for iterator interface."""

    def test_read_all_returns_all_instances(self) -> None:
        """Test that read_all() returns all instances."""
        stream = io.BytesIO()

        instances_data = [
            (1, 1, np.array([[10, 20]], dtype=np.int32)),
            (2, 1, np.array([[30, 40]], dtype=np.int32)),
            (3, 1, np.array([[50, 60]], dtype=np.int32)),
        ]

        with SegmentationResultWriter(1, 100, 100, stream) as writer:
            for class_id, instance_id, points in instances_data:
                writer.append(class_id, instance_id, points)

        # Via transport layer
        with _read_frame_via_transport(stream) as reader:
            instances = reader.read_all()
            assert len(instances) == 3
            for i, (expected_class, expected_inst, expected_points) in enumerate(instances_data):
                assert instances[i].class_id == expected_class
                assert instances[i].instance_id == expected_inst
                np.testing.assert_array_equal(instances[i].points, expected_points)

    def test_iterator_yields_all_instances(self) -> None:
        """Test that iterator yields all instances."""
        stream = io.BytesIO()

        with SegmentationResultWriter(1, 100, 100, stream) as writer:
            writer.append(1, 1, np.array([[10, 20]], dtype=np.int32))
            writer.append(2, 1, np.array([[30, 40]], dtype=np.int32))

        # Via transport layer
        with _read_frame_via_transport(stream) as reader:
            instances = list(reader)
            assert len(instances) == 2
            assert instances[0].class_id == 1
            assert instances[1].class_id == 2


class TestFlush:
    """Tests for flush functionality."""

    def test_flush_without_close_writes_end_marker(self) -> None:
        """Test that flush() writes end marker without closing."""
        stream = io.BytesIO()
        writer = SegmentationResultWriter(1, 100, 100, stream)

        writer.append(1, 1, np.array([[10, 20]], dtype=np.int32))
        writer.flush()

        # Should have data
        assert stream.tell() > 0

        # Can still write more
        writer.append(2, 1, np.array([[30, 40]], dtype=np.int32))
        writer.close()


class TestValidation:
    """Tests for input validation."""

    def test_writer_accepts_all_byte_values(self) -> None:
        """Test that writer accepts class_id and instance_id of 0-255."""
        stream = io.BytesIO()
        writer = SegmentationResultWriter(1, 100, 100, stream)

        points = np.array([[10, 20]], dtype=np.int32)

        # 255 is now valid (no end-marker)
        writer.append(255, 1, points)
        writer.append(1, 255, points)
        writer.append(255, 255, points)
        writer.close()

        # Read back and verify via transport layer
        with _read_frame_via_transport(stream) as reader:
            inst1 = reader.read_next()
            assert inst1 is not None
            assert inst1.class_id == 255
            assert inst1.instance_id == 1

            inst2 = reader.read_next()
            assert inst2 is not None
            assert inst2.class_id == 1
            assert inst2.instance_id == 255

            inst3 = reader.read_next()
            assert inst3 is not None
            assert inst3.class_id == 255
            assert inst3.instance_id == 255

    def test_reader_validates_point_count(self) -> None:
        """Test that reader validates point count."""
        stream = io.BytesIO()

        # Write frame header manually
        stream.write(struct.pack("<Q", 1))  # frame_id
        stream.write(b"\x64")  # width = 100
        stream.write(b"\x64")  # height = 100

        # Write instance with huge point count
        stream.write(bytes([1, 1]))  # class_id, instance_id
        # Write varint for > 10M points (will fail validation)
        # 20M = 0x1312D00
        stream.write(b"\x80\xba\xc8\x89\x01")  # varint encoding of 20000000

        # Read and expect validation error
        stream.seek(0)
        reader = SegmentationResultReader(stream)

        with pytest.raises(ValueError, match="exceeds maximum"):
            reader.read_next()


class TestListConversion:
    """Tests for list conversion."""

    def test_to_list_converts_numpy_to_tuples(self) -> None:
        """Test conversion from NumPy array to list of tuples."""
        points = np.array([[10, 20], [30, 40]], dtype=np.int32)
        instance = SegmentationInstance(1, 1, points)

        points_list = instance.to_list()

        assert points_list == [(10, 20), (30, 40)]
        assert all(isinstance(p, tuple) for p in points_list)


class TestListInput:
    """Tests for list input (not just NumPy arrays)."""

    def test_writer_accepts_list_of_tuples(self) -> None:
        """Test that writer accepts list of tuples."""
        stream = io.BytesIO()
        points_list: List[Tuple[int, int]] = [(10, 20), (30, 40), (50, 60)]

        with SegmentationResultWriter(1, 100, 100, stream) as writer:
            writer.append(1, 1, points_list)

        # Via transport layer
        with _read_frame_via_transport(stream) as reader:
            instance = reader.read_next()
            assert instance is not None
            expected = np.array(points_list, dtype=np.int32)
            np.testing.assert_array_equal(instance.points, expected)


class TestEndianness:
    """Tests for explicit little-endian encoding."""

    def test_frame_id_uses_little_endian(self) -> None:
        """Test that frame_id is encoded as little-endian."""
        stream = io.BytesIO()

        frame_id = 0x0102030405060708  # Distinctive pattern
        with SegmentationResultWriter(frame_id, 100, 100, stream):
            pass

        # Check frame_id via transport layer (skip varint prefix first)
        stream.seek(0)
        frame_source = StreamFrameSource(stream)
        frame_data = frame_source.read_frame()
        assert frame_data is not None

        # First 8 bytes of frame data should be frame_id in little-endian
        frame_id_bytes = frame_data[:8]
        decoded = struct.unpack("<Q", frame_id_bytes)[0]
        assert decoded == frame_id

        # Verify it's different from big-endian
        decoded_big = struct.unpack(">Q", frame_id_bytes)[0]
        assert decoded_big != frame_id


class TestSegmentationFrame:
    """Tests for SegmentationFrame dataclass."""

    def test_frame_properties(self) -> None:
        """Test basic frame properties."""
        instances = [
            SegmentationInstance(1, 1, np.array([[10, 20]], dtype=np.int32)),
            SegmentationInstance(2, 1, np.array([[30, 40], [50, 60]], dtype=np.int32)),
        ]
        frame = SegmentationFrame(frame_id=42, width=1920, height=1080, instances=instances)

        assert frame.frame_id == 42
        assert frame.width == 1920
        assert frame.height == 1080
        assert frame.count == 2

    def test_find_by_class(self) -> None:
        """Test finding instances by class ID."""
        instances = [
            SegmentationInstance(1, 1, np.array([[10, 20]], dtype=np.int32)),
            SegmentationInstance(2, 1, np.array([[30, 40]], dtype=np.int32)),
            SegmentationInstance(1, 2, np.array([[50, 60]], dtype=np.int32)),
        ]
        frame = SegmentationFrame(frame_id=1, width=100, height=100, instances=instances)

        class1_instances = frame.find_by_class(1)
        assert len(class1_instances) == 2
        assert class1_instances[0].instance_id == 1
        assert class1_instances[1].instance_id == 2

        class2_instances = frame.find_by_class(2)
        assert len(class2_instances) == 1

        class3_instances = frame.find_by_class(3)
        assert len(class3_instances) == 0

    def test_find_by_instance(self) -> None:
        """Test finding instances by instance ID."""
        instances = [
            SegmentationInstance(1, 1, np.array([[10, 20]], dtype=np.int32)),
            SegmentationInstance(2, 1, np.array([[30, 40]], dtype=np.int32)),
            SegmentationInstance(1, 2, np.array([[50, 60]], dtype=np.int32)),
        ]
        frame = SegmentationFrame(frame_id=1, width=100, height=100, instances=instances)

        inst1_instances = frame.find_by_instance(1)
        assert len(inst1_instances) == 2

        inst2_instances = frame.find_by_instance(2)
        assert len(inst2_instances) == 1


class TestSegmentationProtocol:
    """Tests for SegmentationProtocol static class."""

    def test_write_read_roundtrip(self) -> None:
        """Test write/read roundtrip through protocol."""
        instances = [
            SegmentationInstance(1, 1, np.array([[10, 20], [30, 40]], dtype=np.int32)),
            SegmentationInstance(2, 1, np.array([[100, 200], [101, 201]], dtype=np.int32)),
        ]
        original = SegmentationFrame(frame_id=42, width=1920, height=1080, instances=instances)

        buffer = bytearray(1000)
        written = SegmentationProtocol.write(buffer, original)
        assert written > 0

        decoded = SegmentationProtocol.read(bytes(buffer[:written]))

        assert decoded.frame_id == original.frame_id
        assert decoded.width == original.width
        assert decoded.height == original.height
        assert len(decoded.instances) == len(original.instances)

        for i, (orig, dec) in enumerate(zip(original.instances, decoded.instances)):
            assert dec.class_id == orig.class_id, f"Instance {i} class mismatch"
            assert dec.instance_id == orig.instance_id, f"Instance {i} id mismatch"
            np.testing.assert_array_equal(dec.points, orig.points)

    def test_write_header(self) -> None:
        """Test writing just the header."""
        buffer = bytearray(100)
        written = SegmentationProtocol.write_header(buffer, 123, 1920, 1080)

        # Should have frame_id (8) + width varint + height varint
        assert written >= 10  # Minimum size

        # Verify frame_id
        frame_id = struct.unpack("<Q", bytes(buffer[:8]))[0]
        assert frame_id == 123

    def test_write_instance(self) -> None:
        """Test writing a single instance."""
        points: List[Tuple[int, int]] = [(10, 20), (30, 40)]
        buffer = bytearray(100)
        written = SegmentationProtocol.write_instance(buffer, 5, 3, points)

        # Should have classId(1) + instanceId(1) + pointCount varint + points
        assert written > 4

    def test_calculate_instance_size(self) -> None:
        """Test size calculation."""
        size = SegmentationProtocol.calculate_instance_size(10)
        # classId(1) + instanceId(1) + pointCount(max 5) + points(max 10 each)
        assert size == 1 + 1 + 5 + (10 * 10)

    def test_try_read_success(self) -> None:
        """Test try_read with valid data."""
        instances = [SegmentationInstance(1, 1, np.array([[10, 20]], dtype=np.int32))]
        original = SegmentationFrame(frame_id=1, width=100, height=100, instances=instances)

        buffer = bytearray(100)
        written = SegmentationProtocol.write(buffer, original)

        result = SegmentationProtocol.try_read(bytes(buffer[:written]))
        assert result is not None
        assert result.frame_id == 1

    def test_try_read_invalid_data(self) -> None:
        """Test try_read with invalid data."""
        result = SegmentationProtocol.try_read(b"\x00\x01\x02")
        assert result is None

    def test_empty_instances(self) -> None:
        """Test roundtrip with empty instances list."""
        original = SegmentationFrame(frame_id=99, width=640, height=480, instances=[])

        buffer = bytearray(100)
        written = SegmentationProtocol.write(buffer, original)

        decoded = SegmentationProtocol.read(bytes(buffer[:written]))
        assert decoded.frame_id == 99
        assert decoded.width == 640
        assert decoded.height == 480
        assert len(decoded.instances) == 0

    def test_empty_points(self) -> None:
        """Test roundtrip with instance having empty points."""
        instances = [SegmentationInstance(1, 1, np.empty((0, 2), dtype=np.int32))]
        original = SegmentationFrame(frame_id=1, width=100, height=100, instances=instances)

        buffer = bytearray(100)
        written = SegmentationProtocol.write(buffer, original)

        decoded = SegmentationProtocol.read(bytes(buffer[:written]))
        assert len(decoded.instances) == 1
        assert len(decoded.instances[0].points) == 0


class TestSegmentationResultSource:
    """Tests for SegmentationResultSource streaming class."""

    def test_read_single_frame(self) -> None:
        """Test reading a single frame from source."""
        # Write frame using existing writer via transport
        stream = io.BytesIO()
        with SegmentationResultWriter(
            42, 1920, 1080, frame_sink=StreamFrameSink(stream, leave_open=True)
        ) as writer:
            writer.append(1, 1, np.array([[10, 20], [30, 40]], dtype=np.int32))
            writer.append(2, 1, np.array([[100, 200]], dtype=np.int32))

        # Read via source
        stream.seek(0)
        frame_source = StreamFrameSource(stream)
        source = SegmentationResultSource(frame_source)

        frames = list(source.read_frames())
        assert len(frames) == 1

        frame = frames[0]
        assert frame.frame_id == 42
        assert frame.width == 1920
        assert frame.height == 1080
        assert frame.count == 2
        assert frame.instances[0].class_id == 1
        assert frame.instances[1].class_id == 2

    def test_read_multiple_frames(self) -> None:
        """Test reading multiple frames from source."""
        stream = io.BytesIO()
        sink = StreamFrameSink(stream, leave_open=True)

        # Write frame 1
        with SegmentationResultWriter(1, 640, 480, frame_sink=sink) as writer:
            writer.append(1, 1, np.array([[10, 20]], dtype=np.int32))

        # Write frame 2
        with SegmentationResultWriter(2, 1920, 1080, frame_sink=sink) as writer:
            writer.append(2, 1, np.array([[100, 200]], dtype=np.int32))
            writer.append(3, 1, np.array([[300, 400]], dtype=np.int32))

        # Read via source
        stream.seek(0)
        frame_source = StreamFrameSource(stream)
        source = SegmentationResultSource(frame_source)

        frames = list(source.read_frames())
        assert len(frames) == 2

        assert frames[0].frame_id == 1
        assert frames[0].count == 1

        assert frames[1].frame_id == 2
        assert frames[1].count == 2

    def test_context_manager(self) -> None:
        """Test using source as context manager."""
        stream = io.BytesIO()
        with SegmentationResultWriter(
            1, 100, 100, frame_sink=StreamFrameSink(stream, leave_open=True)
        ) as writer:
            writer.append(1, 1, np.array([[10, 20]], dtype=np.int32))

        stream.seek(0)
        frame_source = StreamFrameSource(stream)

        with SegmentationResultSource(frame_source) as source:
            frames = list(source.read_frames())
            assert len(frames) == 1

    def test_close_idempotent(self) -> None:
        """Test that close can be called multiple times."""
        stream = io.BytesIO()
        frame_source = StreamFrameSource(stream)
        source = SegmentationResultSource(frame_source)

        source.close()
        source.close()  # Should not raise

    def test_closed_source_raises(self) -> None:
        """Test that reading from closed source raises."""
        stream = io.BytesIO()
        frame_source = StreamFrameSource(stream)
        source = SegmentationResultSource(frame_source)
        source.close()

        with pytest.raises(RuntimeError, match="closed"):
            list(source.read_frames())
