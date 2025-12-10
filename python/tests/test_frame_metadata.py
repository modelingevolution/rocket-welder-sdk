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
        """Test that FRAME_METADATA_SIZE is 16 bytes (only frame_number + timestamp_ns)."""
        assert FRAME_METADATA_SIZE == 16

    def test_timestamp_unavailable_constant(self):
        """Test that TIMESTAMP_UNAVAILABLE is UINT64_MAX."""
        assert TIMESTAMP_UNAVAILABLE == 0xFFFFFFFFFFFFFFFF

    def test_from_bytes_basic(self):
        """Test parsing FrameMetadata from bytes."""
        # Create metadata bytes (16 bytes: frame_number + timestamp_ns)
        frame_number = 42
        timestamp_ns = 1234567890

        data = struct.pack("<QQ", frame_number, timestamp_ns)
        assert len(data) == FRAME_METADATA_SIZE

        metadata = FrameMetadata.from_bytes(data)

        assert metadata.frame_number == 42
        assert metadata.timestamp_ns == 1234567890

    def test_from_bytes_with_memoryview(self):
        """Test parsing from memoryview."""
        data = struct.pack("<QQ", 1, 2)
        metadata = FrameMetadata.from_bytes(memoryview(data))

        assert metadata.frame_number == 1
        assert metadata.timestamp_ns == 2

    def test_from_bytes_too_short(self):
        """Test that from_bytes raises ValueError for short data."""
        with pytest.raises(ValueError, match="at least 16 bytes"):
            FrameMetadata.from_bytes(b"short")

    def test_from_bytes_extra_data(self):
        """Test that extra data after metadata is ignored."""
        data = struct.pack("<QQ", 100, 200) + b"extra_pixel_data"
        metadata = FrameMetadata.from_bytes(data)

        assert metadata.frame_number == 100
        assert metadata.timestamp_ns == 200

    def test_has_timestamp_true(self):
        """Test has_timestamp when timestamp is available."""
        metadata = FrameMetadata(frame_number=0, timestamp_ns=1000000)
        assert metadata.has_timestamp is True

    def test_has_timestamp_false(self):
        """Test has_timestamp when timestamp is unavailable."""
        metadata = FrameMetadata(frame_number=0, timestamp_ns=TIMESTAMP_UNAVAILABLE)
        assert metadata.has_timestamp is False

    def test_timestamp_ms_available(self):
        """Test timestamp_ms when timestamp is available."""
        # 1,000,000 ns = 1 ms
        metadata = FrameMetadata(frame_number=0, timestamp_ns=1_000_000)
        assert metadata.timestamp_ms == pytest.approx(1.0)

    def test_timestamp_ms_unavailable(self):
        """Test timestamp_ms when timestamp is unavailable."""
        metadata = FrameMetadata(frame_number=0, timestamp_ns=TIMESTAMP_UNAVAILABLE)
        assert metadata.timestamp_ms is None

    def test_str_with_timestamp(self):
        """Test string representation with timestamp."""
        metadata = FrameMetadata(frame_number=42, timestamp_ns=1_500_000_000)
        result = str(metadata)
        assert "Frame 42" in result
        assert "1500.000ms" in result

    def test_str_without_timestamp(self):
        """Test string representation without timestamp."""
        metadata = FrameMetadata(frame_number=0, timestamp_ns=TIMESTAMP_UNAVAILABLE)
        result = str(metadata)
        assert "N/A" in result

    def test_frozen_dataclass(self):
        """Test that FrameMetadata is immutable (frozen)."""
        metadata = FrameMetadata(frame_number=0, timestamp_ns=0)
        with pytest.raises(AttributeError):
            metadata.frame_number = 1  # type: ignore


class TestFrameMetadataProtocol:
    """Test FrameMetadata protocol compatibility with C++ struct."""

    def test_struct_layout_matches_cpp(self):
        """Test that Python struct layout matches C++ struct."""
        # C++ struct layout (16 bytes, 8-byte aligned):
        #   [0-7]   frame_number    - uint64_t
        #   [8-15]  timestamp_ns    - uint64_t
        #
        # Note: width, height, format are NOT in FrameMetadata.
        # They come from GstCaps in ZeroBuffer metadata section.

        # Create data with known values at each position
        frame_number = 0x0102030405060708
        timestamp_ns = 0x1112131415161718

        data = struct.pack("<QQ", frame_number, timestamp_ns)

        # Verify byte positions
        assert data[0:8] == struct.pack("<Q", frame_number)  # frame_number at offset 0
        assert data[8:16] == struct.pack("<Q", timestamp_ns)  # timestamp_ns at offset 8

        # Parse and verify
        metadata = FrameMetadata.from_bytes(data)
        assert metadata.frame_number == frame_number
        assert metadata.timestamp_ns == timestamp_ns

    def test_little_endian_parsing(self):
        """Test that parsing uses little-endian byte order."""
        # Little-endian: least significant byte first
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
            ]
        )

        metadata = FrameMetadata.from_bytes(data)
        assert metadata.frame_number == 1
        assert metadata.timestamp_ns == 2
