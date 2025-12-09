"""
Frame metadata structure prepended to each frame in zerobuffer shared memory.

This module provides the FrameMetadata dataclass that matches the C++ struct
defined in frame_metadata.h.

Protocol Layout (24 bytes, 8-byte aligned):
    [0-7]   frame_number    - Sequential frame index (0-based)
    [8-15]  timestamp_ns    - GStreamer PTS in nanoseconds (UINT64_MAX if unavailable)
    [16-17] width           - Frame width in pixels
    [18-19] height          - Frame height in pixels
    [20-21] format          - Pixel format (GstVideoFormat enum value)
    [22-23] reserved        - Alignment padding (must be 0)
"""

from __future__ import annotations

import struct
from dataclasses import dataclass
from typing import ClassVar, Optional

# Size of the FrameMetadata structure in bytes
FRAME_METADATA_SIZE = 24

# Value indicating timestamp is unavailable
TIMESTAMP_UNAVAILABLE = 0xFFFFFFFFFFFFFFFF  # UINT64_MAX

# Struct format: little-endian, 2 uint64 + 4 uint16
# Q = unsigned long long (8 bytes)
# H = unsigned short (2 bytes)
_FRAME_METADATA_FORMAT = "<QQHHHH"


# Common GstVideoFormat values
class GstVideoFormat:
    """Common GStreamer video format values."""

    UNKNOWN = 0
    I420 = 2
    YV12 = 3
    YUY2 = 4
    UYVY = 5
    RGBA = 11
    BGRA = 12
    ARGB = 13
    ABGR = 14
    RGB = 15
    BGR = 16
    NV12 = 23
    NV21 = 24
    GRAY8 = 25
    GRAY16_BE = 26
    GRAY16_LE = 27

    _FORMAT_NAMES: ClassVar[dict[int, str]] = {
        0: "UNKNOWN",
        2: "I420",
        3: "YV12",
        4: "YUY2",
        5: "UYVY",
        11: "RGBA",
        12: "BGRA",
        13: "ARGB",
        14: "ABGR",
        15: "RGB",
        16: "BGR",
        23: "NV12",
        24: "NV21",
        25: "GRAY8",
        26: "GRAY16_BE",
        27: "GRAY16_LE",
    }

    @classmethod
    def to_string(cls, format_value: int) -> str:
        """Convert format value to string name."""
        return cls._FORMAT_NAMES.get(format_value, f"FORMAT_{format_value}")


@dataclass(frozen=True)
class FrameMetadata:
    """
    Frame metadata prepended to each frame in zerobuffer shared memory.

    Attributes:
        frame_number: Sequential frame index (0-based, increments per frame)
        timestamp_ns: GStreamer PTS in nanoseconds (TIMESTAMP_UNAVAILABLE if not set)
        width: Frame width in pixels
        height: Frame height in pixels
        format: Pixel format (GstVideoFormat enum value)
        reserved: Reserved for future use (must be 0)
    """

    frame_number: int
    timestamp_ns: int
    width: int
    height: int
    format: int
    reserved: int = 0

    @classmethod
    def from_bytes(cls, data: bytes | memoryview) -> FrameMetadata:
        """
        Parse FrameMetadata from raw bytes.

        Args:
            data: At least 24 bytes of data

        Returns:
            FrameMetadata instance

        Raises:
            ValueError: If data is too short
        """
        if len(data) < FRAME_METADATA_SIZE:
            raise ValueError(f"Data must be at least {FRAME_METADATA_SIZE} bytes, got {len(data)}")

        # Unpack the struct
        frame_number, timestamp_ns, width, height, fmt, reserved = struct.unpack(
            _FRAME_METADATA_FORMAT, data[:FRAME_METADATA_SIZE]
        )

        return cls(
            frame_number=frame_number,
            timestamp_ns=timestamp_ns,
            width=width,
            height=height,
            format=fmt,
            reserved=reserved,
        )

    @property
    def has_timestamp(self) -> bool:
        """Check if timestamp is available."""
        return self.timestamp_ns != TIMESTAMP_UNAVAILABLE

    @property
    def timestamp_ms(self) -> Optional[float]:
        """Get timestamp in milliseconds, or None if unavailable."""
        if self.has_timestamp:
            return self.timestamp_ns / 1_000_000.0
        return None

    @property
    def format_name(self) -> str:
        """Get the format as a GstVideoFormat name."""
        return GstVideoFormat.to_string(self.format)

    def __str__(self) -> str:
        """Return string representation."""
        timestamp = f"{self.timestamp_ns / 1_000_000.0:.3f}ms" if self.has_timestamp else "N/A"
        return f"Frame {self.frame_number}: {self.width}x{self.height} {self.format_name} @ {timestamp}"
