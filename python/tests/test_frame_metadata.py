"""Tests for rocket_welder_sdk frame_metadata module."""

import struct

import pytest

from rocket_welder_sdk.frame_metadata import (
    FRAME_METADATA_SIZE,
    TIMESTAMP_UNAVAILABLE,
    FrameMetadata,
    GstVideoFormat,
)


class TestGstVideoFormat:
    """Test the GstVideoFormat class."""

    def test_format_constants(self):
        """Test format constant values match GStreamer."""
        assert GstVideoFormat.UNKNOWN == 0
        assert GstVideoFormat.I420 == 2
        assert GstVideoFormat.RGB == 15
        assert GstVideoFormat.BGR == 16
        assert GstVideoFormat.RGBA == 11
        assert GstVideoFormat.BGRA == 12
        assert GstVideoFormat.GRAY8 == 25
        assert GstVideoFormat.NV12 == 23

    def test_to_string_known_format(self):
        """Test to_string for known formats."""
        assert GstVideoFormat.to_string(0) == "UNKNOWN"
        assert GstVideoFormat.to_string(15) == "RGB"
        assert GstVideoFormat.to_string(16) == "BGR"
        assert GstVideoFormat.to_string(25) == "GRAY8"

    def test_to_string_unknown_format(self):
        """Test to_string for unknown formats."""
        assert GstVideoFormat.to_string(999) == "FORMAT_999"


class TestFrameMetadata:
    """Test the FrameMetadata dataclass."""

    def test_size_constant(self):
        """Test that FRAME_METADATA_SIZE is 24 bytes."""
        assert FRAME_METADATA_SIZE == 24

    def test_timestamp_unavailable_constant(self):
        """Test that TIMESTAMP_UNAVAILABLE is UINT64_MAX."""
        assert TIMESTAMP_UNAVAILABLE == 0xFFFFFFFFFFFFFFFF

    def test_from_bytes_basic(self):
        """Test parsing FrameMetadata from bytes."""
        # Create metadata bytes
        frame_number = 42
        timestamp_ns = 1234567890
        width = 640
        height = 480
        fmt = 15  # RGB
        reserved = 0

        data = struct.pack("<QQHHHH", frame_number, timestamp_ns, width, height, fmt, reserved)
        assert len(data) == FRAME_METADATA_SIZE

        metadata = FrameMetadata.from_bytes(data)

        assert metadata.frame_number == 42
        assert metadata.timestamp_ns == 1234567890
        assert metadata.width == 640
        assert metadata.height == 480
        assert metadata.format == 15
        assert metadata.reserved == 0

    def test_from_bytes_with_memoryview(self):
        """Test parsing from memoryview."""
        data = struct.pack("<QQHHHH", 1, 2, 3, 4, 5, 0)
        metadata = FrameMetadata.from_bytes(memoryview(data))

        assert metadata.frame_number == 1
        assert metadata.timestamp_ns == 2
        assert metadata.width == 3
        assert metadata.height == 4
        assert metadata.format == 5

    def test_from_bytes_too_short(self):
        """Test that from_bytes raises ValueError for short data."""
        with pytest.raises(ValueError, match="at least 24 bytes"):
            FrameMetadata.from_bytes(b"short")

    def test_from_bytes_extra_data(self):
        """Test that extra data after metadata is ignored."""
        data = struct.pack("<QQHHHH", 100, 200, 300, 400, 15, 0) + b"extra"
        metadata = FrameMetadata.from_bytes(data)

        assert metadata.frame_number == 100
        assert metadata.width == 300

    def test_has_timestamp_true(self):
        """Test has_timestamp when timestamp is available."""
        metadata = FrameMetadata(
            frame_number=0, timestamp_ns=1000000, width=640, height=480, format=15
        )
        assert metadata.has_timestamp is True

    def test_has_timestamp_false(self):
        """Test has_timestamp when timestamp is unavailable."""
        metadata = FrameMetadata(
            frame_number=0, timestamp_ns=TIMESTAMP_UNAVAILABLE, width=640, height=480, format=15
        )
        assert metadata.has_timestamp is False

    def test_timestamp_ms_available(self):
        """Test timestamp_ms when timestamp is available."""
        # 1,000,000 ns = 1 ms
        metadata = FrameMetadata(
            frame_number=0, timestamp_ns=1_000_000, width=640, height=480, format=15
        )
        assert metadata.timestamp_ms == pytest.approx(1.0)

    def test_timestamp_ms_unavailable(self):
        """Test timestamp_ms when timestamp is unavailable."""
        metadata = FrameMetadata(
            frame_number=0, timestamp_ns=TIMESTAMP_UNAVAILABLE, width=640, height=480, format=15
        )
        assert metadata.timestamp_ms is None

    def test_format_name(self):
        """Test format_name property."""
        metadata = FrameMetadata(frame_number=0, timestamp_ns=0, width=640, height=480, format=15)
        assert metadata.format_name == "RGB"

        metadata2 = FrameMetadata(frame_number=0, timestamp_ns=0, width=640, height=480, format=25)
        assert metadata2.format_name == "GRAY8"

    def test_str_with_timestamp(self):
        """Test string representation with timestamp."""
        metadata = FrameMetadata(
            frame_number=42, timestamp_ns=1_500_000_000, width=1920, height=1080, format=16
        )
        result = str(metadata)
        assert "Frame 42" in result
        assert "1920x1080" in result
        assert "BGR" in result
        assert "1500.000ms" in result

    def test_str_without_timestamp(self):
        """Test string representation without timestamp."""
        metadata = FrameMetadata(
            frame_number=0, timestamp_ns=TIMESTAMP_UNAVAILABLE, width=640, height=480, format=15
        )
        result = str(metadata)
        assert "N/A" in result

    def test_frozen_dataclass(self):
        """Test that FrameMetadata is immutable (frozen)."""
        metadata = FrameMetadata(frame_number=0, timestamp_ns=0, width=640, height=480, format=15)
        with pytest.raises(AttributeError):
            metadata.frame_number = 1  # type: ignore


class TestFrameMetadataProtocol:
    """Test FrameMetadata protocol compatibility with C++ struct."""

    def test_struct_layout_matches_cpp(self):
        """Test that Python struct layout matches C++ struct."""
        # C++ struct layout (24 bytes, 8-byte aligned):
        #   [0-7]   frame_number    - uint64_t
        #   [8-15]  timestamp_ns    - uint64_t
        #   [16-17] width           - uint16_t
        #   [18-19] height          - uint16_t
        #   [20-21] format          - uint16_t
        #   [22-23] reserved        - uint16_t

        # Create data with known values at each position
        frame_number = 0x0102030405060708
        timestamp_ns = 0x1112131415161718
        width = 0x2122
        height = 0x3132
        fmt = 0x4142
        reserved = 0x5152

        data = struct.pack("<QQHHHH", frame_number, timestamp_ns, width, height, fmt, reserved)

        # Verify byte positions
        assert data[0:8] == struct.pack("<Q", frame_number)  # frame_number at offset 0
        assert data[8:16] == struct.pack("<Q", timestamp_ns)  # timestamp_ns at offset 8
        assert data[16:18] == struct.pack("<H", width)  # width at offset 16
        assert data[18:20] == struct.pack("<H", height)  # height at offset 18
        assert data[20:22] == struct.pack("<H", fmt)  # format at offset 20
        assert data[22:24] == struct.pack("<H", reserved)  # reserved at offset 22

        # Parse and verify
        metadata = FrameMetadata.from_bytes(data)
        assert metadata.frame_number == frame_number
        assert metadata.timestamp_ns == timestamp_ns
        assert metadata.width == width
        assert metadata.height == height
        assert metadata.format == fmt
        assert metadata.reserved == reserved

    def test_little_endian_parsing(self):
        """Test that parsing uses little-endian byte order."""
        # Little-endian: least significant byte first
        # Value 0x0102 in little-endian: bytes [0x02, 0x01]
        data = bytes(
            [
                # frame_number = 1 (little-endian uint64)
                0x01,
                0x00,
                0x00,
                0x00,
                0x00,
                0x00,
                0x00,
                0x00,
                # timestamp_ns = 2 (little-endian uint64)
                0x02,
                0x00,
                0x00,
                0x00,
                0x00,
                0x00,
                0x00,
                0x00,
                # width = 640 (0x0280 in little-endian: [0x80, 0x02])
                0x80,
                0x02,
                # height = 480 (0x01E0 in little-endian: [0xE0, 0x01])
                0xE0,
                0x01,
                # format = 15 (RGB)
                0x0F,
                0x00,
                # reserved = 0
                0x00,
                0x00,
            ]
        )

        metadata = FrameMetadata.from_bytes(data)
        assert metadata.frame_number == 1
        assert metadata.timestamp_ns == 2
        assert metadata.width == 640
        assert metadata.height == 480
        assert metadata.format == 15
        assert metadata.reserved == 0
