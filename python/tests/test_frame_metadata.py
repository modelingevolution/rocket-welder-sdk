"""Tests for rocket_welder_sdk frame_metadata module."""

import struct

import pytest

from rocket_welder_sdk.frame_metadata import (
    EXPOSURE_TIME_UNAVAILABLE,
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
        """FrameMetadata is 24 bytes (frame_number + timestamp_ns + exposure_time_us)."""
        assert FRAME_METADATA_SIZE == 24

    def test_timestamp_unavailable_constant(self):
        """Test that TIMESTAMP_UNAVAILABLE is UINT64_MAX."""
        assert TIMESTAMP_UNAVAILABLE == 0xFFFFFFFFFFFFFFFF

    def test_exposure_time_unavailable_constant(self):
        """Test that EXPOSURE_TIME_UNAVAILABLE is UINT64_MAX."""
        assert EXPOSURE_TIME_UNAVAILABLE == 0xFFFFFFFFFFFFFFFF

    def test_from_bytes_basic(self):
        """Test parsing FrameMetadata from bytes."""
        data = struct.pack("<QQQ", 42, 1234567890, 5000)
        assert len(data) == FRAME_METADATA_SIZE

        metadata = FrameMetadata.from_bytes(data)

        assert metadata.frame_number == 42
        assert metadata.timestamp_ns == 1234567890
        assert metadata.exposure_time_us == 5000

    def test_from_bytes_with_memoryview(self):
        """Test parsing from memoryview."""
        data = struct.pack("<QQQ", 1, 2, 3)
        metadata = FrameMetadata.from_bytes(memoryview(data))

        assert metadata.frame_number == 1
        assert metadata.timestamp_ns == 2
        assert metadata.exposure_time_us == 3

    def test_from_bytes_too_short(self):
        """Test that from_bytes raises ValueError for short data."""
        with pytest.raises(ValueError, match="at least 24 bytes"):
            FrameMetadata.from_bytes(b"short")

    def test_from_bytes_extra_data(self):
        """Test that extra data after metadata is ignored."""
        data = struct.pack("<QQQ", 100, 200, 300) + b"extra_pixel_data"
        metadata = FrameMetadata.from_bytes(data)

        assert metadata.frame_number == 100
        assert metadata.timestamp_ns == 200
        assert metadata.exposure_time_us == 300

    def test_has_timestamp_true(self):
        """Test has_timestamp when timestamp is available."""
        metadata = FrameMetadata(frame_number=0, timestamp_ns=1000000, exposure_time_us=0)
        assert metadata.has_timestamp is True

    def test_has_timestamp_false(self):
        """Test has_timestamp when timestamp is unavailable."""
        metadata = FrameMetadata(
            frame_number=0, timestamp_ns=TIMESTAMP_UNAVAILABLE, exposure_time_us=0
        )
        assert metadata.has_timestamp is False

    def test_has_exposure_time_true(self):
        """Test has_exposure_time when exposure time is available."""
        metadata = FrameMetadata(frame_number=0, timestamp_ns=0, exposure_time_us=500)
        assert metadata.has_exposure_time is True

    def test_has_exposure_time_false(self):
        """Test has_exposure_time when exposure time is unavailable."""
        metadata = FrameMetadata(
            frame_number=0,
            timestamp_ns=0,
            exposure_time_us=EXPOSURE_TIME_UNAVAILABLE,
        )
        assert metadata.has_exposure_time is False

    def test_timestamp_ms_available(self):
        """Test timestamp_ms when timestamp is available."""
        # 1,000,000 ns = 1 ms
        metadata = FrameMetadata(frame_number=0, timestamp_ns=1_000_000, exposure_time_us=0)
        assert metadata.timestamp_ms == pytest.approx(1.0)

    def test_timestamp_ms_unavailable(self):
        """Test timestamp_ms when timestamp is unavailable."""
        metadata = FrameMetadata(
            frame_number=0, timestamp_ns=TIMESTAMP_UNAVAILABLE, exposure_time_us=0
        )
        assert metadata.timestamp_ms is None

    def test_exposure_time_ms(self):
        """Test exposure_time_ms when exposure time is available."""
        metadata = FrameMetadata(frame_number=0, timestamp_ns=0, exposure_time_us=5000)
        assert metadata.exposure_time_ms == pytest.approx(5.0)

    def test_exposure_time_ms_unavailable(self):
        """Test exposure_time_ms when exposure time is unavailable."""
        metadata = FrameMetadata(
            frame_number=0, timestamp_ns=0, exposure_time_us=EXPOSURE_TIME_UNAVAILABLE
        )
        assert metadata.exposure_time_ms is None

    def test_str_with_timestamp(self):
        """Test string representation with timestamp."""
        metadata = FrameMetadata(frame_number=42, timestamp_ns=1_500_000_000, exposure_time_us=5000)
        result = str(metadata)
        assert "Frame 42" in result
        assert "1500.000ms" in result
        assert "exp=5000" in result

    def test_str_without_timestamp(self):
        """Test string representation without timestamp."""
        metadata = FrameMetadata(
            frame_number=0,
            timestamp_ns=TIMESTAMP_UNAVAILABLE,
            exposure_time_us=EXPOSURE_TIME_UNAVAILABLE,
        )
        result = str(metadata)
        assert "N/A" in result
        assert "exp=N/A" in result

    def test_frozen_dataclass(self):
        """Test that FrameMetadata is immutable (frozen)."""
        metadata = FrameMetadata(frame_number=0, timestamp_ns=0, exposure_time_us=0)
        with pytest.raises(AttributeError):
            metadata.frame_number = 1  # type: ignore


class TestFrameMetadataProtocol:
    """Test FrameMetadata protocol compatibility with C++ struct."""

    def test_struct_layout_matches_cpp(self):
        """Python struct layout matches C++ struct (24 bytes, 8-byte aligned)."""
        frame_number = 0x0102030405060708
        timestamp_ns = 0x1112131415161718
        exposure_time_us = 0x2122232425262728

        data = struct.pack("<QQQ", frame_number, timestamp_ns, exposure_time_us)

        assert data[0:8] == struct.pack("<Q", frame_number)  # offset 0
        assert data[8:16] == struct.pack("<Q", timestamp_ns)  # offset 8
        assert data[16:24] == struct.pack("<Q", exposure_time_us)  # offset 16

        metadata = FrameMetadata.from_bytes(data)
        assert metadata.frame_number == frame_number
        assert metadata.timestamp_ns == timestamp_ns
        assert metadata.exposure_time_us == exposure_time_us

    def test_little_endian_parsing(self):
        """Test that parsing uses little-endian byte order."""
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
                # exposure_time_us = 3 (little-endian uint64)
                0x03,
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
        assert metadata.exposure_time_us == 3
