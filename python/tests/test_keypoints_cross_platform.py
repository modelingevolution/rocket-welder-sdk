"""Cross-platform integration tests for keypoints protocol.

Tests interoperability between C# and Python implementations.
"""

import json
import tempfile
from pathlib import Path

import pytest

from rocket_welder_sdk.keypoints_protocol import KeyPointsSink


class TestCrossPlatform:
    """Cross-platform interoperability tests."""

    @pytest.fixture
    def test_dir(self) -> Path:
        """Get shared test directory."""
        return Path(tempfile.gettempdir()) / "rocket-welder-test"

    def test_read_csharp_written_file(self, test_dir: Path) -> None:
        """Test that Python can read file written by C#."""
        test_file = test_dir / "csharp_to_python_keypoints.bin"
        json_file = test_dir / "keypoints_definition.json"

        # Skip if C# hasn't run yet
        if not test_file.exists() or not json_file.exists():
            pytest.skip(
                f"C# test files not found: {test_file}, {json_file}. "
                "Run C# tests first to generate test files."
            )

        # Read JSON definition
        with open(json_file) as f:
            json_def = f.read()

        # Expected metadata (must match C# test)
        definition = json.loads(json_def)
        assert definition["version"] == "1.0"
        assert definition["compute_module_name"] == "TestModel"
        assert "nose" in definition["points"]
        assert "left_eye" in definition["points"]

        # Act - Python reads C# file
        with open(test_file, "rb") as f:
            storage = KeyPointsSink(f)
            series = storage.read(json_def, f)

            # Verify metadata
            assert series.version == "1.0"
            assert series.compute_module_name == "TestModel"
            assert len(series.points) == 5

            # Verify frames exist
            assert series.contains_frame(0)
            assert series.contains_frame(1)
            assert series.contains_frame(2)

            # Verify frame 0 (master frame)
            frame0 = series.get_frame(0)
            assert frame0 is not None
            assert len(frame0) == 2

            # Verify keypoint data from C# (frame 0, keypoint 0)
            point, conf = frame0[0]
            assert point == (100, 200)
            assert abs(conf - 0.95) < 0.0001

            # Verify frame 1 (delta frame) - delta decoded correctly
            frame1 = series.get_frame(1)
            assert frame1 is not None
            point, conf = frame1[0]
            assert point == (101, 201)
            assert abs(conf - 0.94) < 0.0001

    def test_write_for_csharp_to_read(self, test_dir: Path) -> None:
        """Test that Python writes file that C# can read."""
        test_dir.mkdir(exist_ok=True)
        test_file = test_dir / "python_to_csharp_keypoints.bin"
        json_file = test_dir / "keypoints_definition_python.json"

        # Arrange - test data
        json_def = {
            "version": "1.0",
            "compute_module_name": "PythonTestModel",
            "points": {
                "nose": 0,
                "left_eye": 1,
                "right_eye": 2,
                "left_shoulder": 3,
                "right_shoulder": 4,
            },
        }

        # Write JSON definition
        with open(json_file, "w") as f:
            json.dump(json_def, f, indent=2)

        # Act - Python writes keypoints
        with open(test_file, "wb") as f:
            storage = KeyPointsSink(f, master_frame_interval=2)

            # Frame 0 - Master
            with storage.create_writer(frame_id=0) as writer:
                writer.append(0, 100, 200, 0.95)
                writer.append(1, 120, 190, 0.92)
                writer.append(2, 80, 190, 0.88)

            # Frame 1 - Delta
            with storage.create_writer(frame_id=1) as writer:
                writer.append(0, 101, 201, 0.94)
                writer.append(1, 121, 191, 0.93)
                writer.append(2, 81, 191, 0.89)

            # Frame 2 - Master
            with storage.create_writer(frame_id=2) as writer:
                writer.append(0, 105, 205, 0.96)
                writer.append(1, 125, 195, 0.91)

        # Verify files exist and have data
        assert test_file.exists()
        assert json_file.exists()
        file_size = test_file.stat().st_size
        assert file_size > 0

        print(f"Python wrote test file: {test_file}")
        print(f"Python wrote JSON: {json_file}")
        print(f"File size: {file_size} bytes")
        print("Frames: 3, Keypoints per frame: 3, 3, 2")

        # C# will read and verify this file in its test suite

    def test_roundtrip_python_write_python_read(self, test_dir: Path) -> None:
        """Test Python writes and reads its own file (baseline)."""
        test_dir.mkdir(exist_ok=True)
        test_file = test_dir / "python_roundtrip_keypoints.bin"

        # Arrange
        json_def = json.dumps(
            {
                "version": "1.0",
                "compute_module_name": "RoundtripTest",
                "points": {"nose": 0, "left_eye": 1, "right_eye": 2},
            }
        )

        # Act - Write
        with open(test_file, "wb") as f:
            storage = KeyPointsSink(f)

            with storage.create_writer(frame_id=1) as writer:
                writer.append(0, 100, 200, 0.95)
                writer.append(1, 120, 190, 0.92)

            with storage.create_writer(frame_id=2) as writer:
                writer.append(0, 110, 210, 0.94)
                writer.append(1, 130, 200, 0.93)

        # Act - Read
        with open(test_file, "rb") as f:
            storage = KeyPointsSink(f)
            series = storage.read(json_def, f)

            # Verify
            assert series.version == "1.0"
            assert series.compute_module_name == "RoundtripTest"
            assert len(series.frame_ids) == 2

            # Verify frame 1
            frame1 = series.get_frame(1)
            assert frame1 is not None
            point, conf = frame1[0]
            assert point == (100, 200)
            assert abs(conf - 0.95) < 0.0001

            # Verify frame 2
            frame2 = series.get_frame(2)
            assert frame2 is not None
            point, conf = frame2[0]
            assert point == (110, 210)
            assert abs(conf - 0.94) < 0.0001

    def test_master_delta_compression_efficiency(self, test_dir: Path) -> None:
        """Test that delta encoding provides compression benefits."""
        test_dir.mkdir(exist_ok=True)

        # Write with all master frames (no compression)
        test_file_all_master = test_dir / "all_master.bin"

        with open(test_file_all_master, "wb") as f:
            storage = KeyPointsSink(f, master_frame_interval=1)
            for frame_id in range(10):
                with storage.create_writer(frame_id=frame_id) as writer:
                    writer.append(0, 100 + frame_id, 200 + frame_id, 0.95)

        all_master_size = test_file_all_master.stat().st_size

        # Write with delta frames (with compression)
        test_file_with_delta = test_dir / "with_delta.bin"

        with open(test_file_with_delta, "wb") as f:
            storage = KeyPointsSink(f, master_frame_interval=300)
            for frame_id in range(10):
                with storage.create_writer(frame_id=frame_id) as writer:
                    writer.append(0, 100 + frame_id, 200 + frame_id, 0.95)

        with_delta_size = test_file_with_delta.stat().st_size

        # Delta should be smaller
        print(f"All master frames: {all_master_size} bytes")
        print(f"With delta frames: {with_delta_size} bytes")
        print(f"Compression ratio: {all_master_size / with_delta_size:.2f}x")

        assert with_delta_size < all_master_size, "Delta encoding should reduce file size"
