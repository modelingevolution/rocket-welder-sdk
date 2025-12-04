"""Cross-platform integration tests for segmentation results.

Tests interoperability between C# and Python implementations.
"""

import io
import tempfile
from pathlib import Path

import numpy as np
import pytest

from rocket_welder_sdk.segmentation_result import (
    SegmentationResultReader,
    SegmentationResultWriter,
)
from rocket_welder_sdk.transport import StreamFrameSource


def _read_frame_via_transport(stream: io.IOBase) -> SegmentationResultReader:
    """Helper to read a single frame via transport layer (handles varint framing)."""
    frame_source = StreamFrameSource(stream, leave_open=True)  # type: ignore[arg-type]
    frame_data = frame_source.read_frame()
    if frame_data is None:
        raise ValueError("No frame data found")
    return SegmentationResultReader(io.BytesIO(frame_data))


class TestCrossPlatform:
    """Cross-platform interoperability tests."""

    @pytest.fixture
    def test_dir(self) -> Path:
        """Get shared test directory."""
        return Path(tempfile.gettempdir()) / "rocket-welder-test"

    def test_read_csharp_written_file(self, test_dir: Path) -> None:
        """Test that Python can read file written by C#."""
        test_file = test_dir / "csharp_to_python.bin"

        # Expected data (must match C# test)
        expected_frame_id = 12345
        expected_width = 640
        expected_height = 480
        expected_instances = [
            (1, 1, np.array([[10, 20], [30, 40]], dtype=np.int32)),
            (2, 1, np.array([[100, 200], [150, 250], [200, 300]], dtype=np.int32)),
            (1, 2, np.array([[500, 400]], dtype=np.int32)),
        ]

        # Skip if C# hasn't run yet
        if not test_file.exists():
            pytest.skip(
                f"C# test file not found: {test_file}. " "Run C# tests first to generate test file."
            )

        # Act - Python reads C# file (via transport layer for framing)
        with open(test_file, "rb") as f:
            reader = _read_frame_via_transport(f)
            metadata = reader.metadata

            # Verify metadata
            assert metadata.frame_id == expected_frame_id
            assert metadata.width == expected_width
            assert metadata.height == expected_height

            # Verify instances
            instances = reader.read_all()
            assert len(instances) == len(expected_instances)

            for i, (expected_class, expected_inst, expected_points) in enumerate(
                expected_instances
            ):
                assert instances[i].class_id == expected_class
                assert instances[i].instance_id == expected_inst
                np.testing.assert_array_equal(instances[i].points, expected_points)

    def test_write_for_csharp_to_read(self, test_dir: Path) -> None:
        """Test that Python writes file that C# can read."""
        test_dir.mkdir(exist_ok=True)
        test_file = test_dir / "python_to_csharp.bin"

        # Arrange - test data
        frame_id = 54321
        width = 1920
        height = 1080

        instances = [
            (3, 1, np.array([[50, 100], [60, 110], [70, 120]], dtype=np.int32)),
            (4, 1, np.array([[300, 400]], dtype=np.int32)),
            (3, 2, np.array([[800, 900], [810, 910]], dtype=np.int32)),
        ]

        # Act - Python writes
        with open(test_file, "wb") as f, SegmentationResultWriter(
            frame_id, width, height, f
        ) as writer:
            for class_id, instance_id, points in instances:
                writer.append(class_id, instance_id, points)

        # Verify file exists and has data
        assert test_file.exists()
        file_size = test_file.stat().st_size
        assert file_size > 0

        print(f"Python wrote test file: {test_file}")
        print(f"File size: {file_size} bytes")
        print(f"Frame: {frame_id}, Size: {width}x{height}, Instances: {len(instances)}")

        # C# will read and verify this file in its test suite

    def test_roundtrip_python_write_python_read(self, test_dir: Path) -> None:
        """Test Python writes and reads its own file (baseline)."""
        test_dir.mkdir(exist_ok=True)
        test_file = test_dir / "python_roundtrip.bin"

        # Arrange
        frame_id = 99999
        width = 800
        height = 600

        instances = [
            (5, 1, np.array([[10, 20], [30, 40]], dtype=np.int32)),
            (6, 1, np.array([[100, 200]], dtype=np.int32)),
        ]

        # Act - Write
        with open(test_file, "wb") as f, SegmentationResultWriter(
            frame_id, width, height, f
        ) as writer:
            for class_id, instance_id, points in instances:
                writer.append(class_id, instance_id, points)

        # Act - Read (via transport layer for framing)
        with open(test_file, "rb") as f:
            reader = _read_frame_via_transport(f)
            metadata = reader.metadata
            assert metadata.frame_id == frame_id
            assert metadata.width == width
            assert metadata.height == height

            read_instances = reader.read_all()
            assert len(read_instances) == len(instances)

            for i, (expected_class, expected_inst, expected_points) in enumerate(instances):
                assert read_instances[i].class_id == expected_class
                assert read_instances[i].instance_id == expected_inst
                np.testing.assert_array_equal(read_instances[i].points, expected_points)
