"""
GStreamer metadata structures for RocketWelder SDK.
Matches C# GstCaps and GstMetadata functionality.
"""

from __future__ import annotations

import json
import re
from dataclasses import dataclass
from typing import Any

import numpy as np
import numpy.typing as npt


@dataclass
class GstCaps:
    """
    GStreamer capabilities representation.

    Represents video format capabilities including format, dimensions, framerate, etc.
    Matches the C# GstCaps implementation with proper parsing and numpy integration.
    """

    width: int
    height: int
    format: str
    depth_type: type[np.uint8] | type[np.uint16]
    channels: int
    bytes_per_pixel: int
    framerate_num: int | None = None
    framerate_den: int | None = None
    interlace_mode: str | None = None
    colorimetry: str | None = None
    caps_string: str | None = None

    @property
    def frame_size(self) -> int:
        """Calculate the expected frame size in bytes."""
        return self.width * self.height * self.bytes_per_pixel

    @property
    def framerate(self) -> float | None:
        """Get framerate as double (FPS)."""
        if (
            self.framerate_num is not None
            and self.framerate_den is not None
            and self.framerate_den > 0
        ):
            return self.framerate_num / self.framerate_den
        return None

    @classmethod
    def parse(cls, caps_string: str) -> GstCaps:
        """
        Parse GStreamer caps string.
        Example: "video/x-raw, format=(string)RGB, width=(int)640, height=(int)480, framerate=(fraction)30/1"

        Args:
            caps_string: GStreamer caps string

        Returns:
            GstCaps instance

        Raises:
            ValueError: If caps string is invalid
        """
        if not caps_string or not caps_string.strip():
            raise ValueError("Empty caps string")

        caps_string = caps_string.strip()

        # Check if it's a video caps
        if not caps_string.startswith("video/x-raw"):
            raise ValueError(f"Not a video/x-raw caps string: {caps_string}")

        try:
            # Parse width
            width_match = re.search(r"width=\(int\)(\d+)", caps_string)
            if not width_match:
                raise ValueError("Missing width in caps string")
            width = int(width_match.group(1))

            # Parse height
            height_match = re.search(r"height=\(int\)(\d+)", caps_string)
            if not height_match:
                raise ValueError("Missing height in caps string")
            height = int(height_match.group(1))

            # Parse format
            format_match = re.search(r"format=\(string\)(\w+)", caps_string)
            format_str = format_match.group(1) if format_match else "RGB"

            # Parse framerate (optional)
            framerate_num = None
            framerate_den = None
            framerate_match = re.search(r"framerate=\(fraction\)(\d+)/(\d+)", caps_string)
            if framerate_match:
                framerate_num = int(framerate_match.group(1))
                framerate_den = int(framerate_match.group(2))

            # Parse interlace mode (optional)
            interlace_mode = None
            interlace_match = re.search(r"interlace-mode=\(string\)(\w+)", caps_string)
            if interlace_match:
                interlace_mode = interlace_match.group(1)

            # Parse colorimetry (optional)
            colorimetry = None
            colorimetry_match = re.search(r"colorimetry=\(string\)([\w:]+)", caps_string)
            if colorimetry_match:
                colorimetry = colorimetry_match.group(1)

            # Map format to numpy dtype and get channel info
            depth_type, channels, bytes_per_pixel = cls._map_gstreamer_format_to_numpy(format_str)

            return cls(
                width=width,
                height=height,
                format=format_str,
                depth_type=depth_type,
                channels=channels,
                bytes_per_pixel=bytes_per_pixel,
                framerate_num=framerate_num,
                framerate_den=framerate_den,
                interlace_mode=interlace_mode,
                colorimetry=colorimetry,
                caps_string=caps_string,
            )
        except Exception as e:
            raise ValueError(f"Failed to parse caps string: {caps_string}") from e

    @classmethod
    def from_simple(cls, width: int, height: int, format: str = "RGB") -> GstCaps:
        """
        Create GstCaps from simple parameters.

        Args:
            width: Frame width
            height: Frame height
            format: Pixel format (default: "RGB")

        Returns:
            GstCaps instance
        """
        depth_type, channels, bytes_per_pixel = cls._map_gstreamer_format_to_numpy(format)
        return cls(
            width=width,
            height=height,
            format=format,
            depth_type=depth_type,
            channels=channels,
            bytes_per_pixel=bytes_per_pixel,
        )

    @staticmethod
    def _map_gstreamer_format_to_numpy(
        format: str,
    ) -> tuple[type[np.uint8] | type[np.uint16], int, int]:
        """
        Map GStreamer format strings to numpy dtype.
        Reference: https://gstreamer.freedesktop.org/documentation/video/video-format.html

        Args:
            format: GStreamer format string

        Returns:
            Tuple of (numpy dtype, channels, bytes_per_pixel)
        """
        format_upper = format.upper() if format else "RGB"

        format_map = {
            # RGB formats
            "RGB": (np.uint8, 3, 3),
            "BGR": (np.uint8, 3, 3),
            "RGBA": (np.uint8, 4, 4),
            "BGRA": (np.uint8, 4, 4),
            "ARGB": (np.uint8, 4, 4),
            "ABGR": (np.uint8, 4, 4),
            "RGBX": (np.uint8, 4, 4),  # RGB with padding
            "BGRX": (np.uint8, 4, 4),  # BGR with padding
            "XRGB": (np.uint8, 4, 4),  # RGB with padding
            "XBGR": (np.uint8, 4, 4),  # BGR with padding
            # 16-bit RGB formats
            "RGB16": (np.uint16, 3, 6),
            "BGR16": (np.uint16, 3, 6),
            # Grayscale formats
            "GRAY8": (np.uint8, 1, 1),
            "GRAY16_LE": (np.uint16, 1, 2),
            "GRAY16_BE": (np.uint16, 1, 2),
            # YUV planar formats (Y plane only for simplicity)
            "I420": (np.uint8, 1, 1),
            "YV12": (np.uint8, 1, 1),
            "NV12": (np.uint8, 1, 1),
            "NV21": (np.uint8, 1, 1),
            # YUV packed formats
            "YUY2": (np.uint8, 2, 2),
            "UYVY": (np.uint8, 2, 2),
            "YVYU": (np.uint8, 2, 2),
            # Bayer formats (raw sensor data)
            "BGGR": (np.uint8, 1, 1),
            "RGGB": (np.uint8, 1, 1),
            "GRBG": (np.uint8, 1, 1),
            "GBRG": (np.uint8, 1, 1),
        }

        # Default to RGB if unknown
        return format_map.get(format_upper, (np.uint8, 3, 3))

    def create_array(
        self, data: bytes | memoryview | npt.NDArray[np.uint8] | npt.NDArray[np.uint16]
    ) -> npt.NDArray[np.uint8] | npt.NDArray[np.uint16]:
        """
        Create numpy array with proper format from data.

        Args:
            data: Frame data as bytes, memoryview, or existing numpy array

        Returns:
            Numpy array with proper shape and dtype

        Raises:
            ValueError: If data size doesn't match expected frame size
        """
        # Convert memoryview to bytes if needed
        if isinstance(data, memoryview):
            data = bytes(data)

        # If it's already a numpy array, check size and reshape if needed
        if isinstance(data, np.ndarray):
            if data.size * data.itemsize != self.frame_size:
                raise ValueError(
                    f"Data size mismatch. Expected {self.frame_size} bytes for "
                    f"{self.width}x{self.height} {self.format}, got {data.size * data.itemsize}"
                )
            # Reshape if needed
            if self.channels == 1:
                return data.reshape((self.height, self.width))
            else:
                return data.reshape((self.height, self.width, self.channels))

        # Check data size
        if len(data) != self.frame_size:
            raise ValueError(
                f"Data size mismatch. Expected {self.frame_size} bytes for "
                f"{self.width}x{self.height} {self.format}, got {len(data)}"
            )

        # Create array from bytes
        arr = np.frombuffer(data, dtype=self.depth_type)

        # Reshape based on channels
        if self.channels == 1:
            return arr.reshape((self.height, self.width))
        else:
            # For multi-channel images, reshape to (height, width, channels)
            total_pixels = self.width * self.height * self.channels
            if self.depth_type == np.uint16:
                # For 16-bit formats, we need to account for the item size
                arr = arr[:total_pixels]
            return arr.reshape((self.height, self.width, self.channels))

    def create_array_from_pointer(
        self, ptr: int, copy: bool = False
    ) -> npt.NDArray[np.uint8] | npt.NDArray[np.uint16]:
        """
        Create numpy array from memory pointer (zero-copy by default).

        Args:
            ptr: Memory pointer as integer
            copy: If True, make a copy of the data; if False, create a view

        Returns:
            Numpy array with proper shape and dtype
        """
        # Calculate total elements based on depth type
        if self.depth_type == np.uint16:
            total_elements = self.width * self.height * self.channels
        else:
            total_elements = self.frame_size

        # Create array from pointer using ctypes
        import ctypes

        # Create a buffer from the pointer
        buffer_size = total_elements * self.depth_type.itemsize
        c_buffer = (ctypes.c_byte * buffer_size).from_address(ptr)
        arr = np.frombuffer(c_buffer, dtype=self.depth_type)

        # Reshape based on channels
        if self.channels == 1:
            shaped = arr.reshape((self.height, self.width))
        else:
            shaped = arr.reshape((self.height, self.width, self.channels))

        return shaped.copy() if copy else shaped

    def __str__(self) -> str:
        """String representation."""
        # If we have the original caps string, return it for perfect round-tripping
        if self.caps_string:
            return self.caps_string

        # Otherwise build a simple display string
        fps = f" @ {self.framerate:.2f}fps" if self.framerate else ""
        return f"{self.width}x{self.height} {self.format}{fps}"

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary representation."""
        result = {
            "width": self.width,
            "height": self.height,
            "format": self.format,
            "channels": self.channels,
            "bytes_per_pixel": self.bytes_per_pixel,
        }

        if self.framerate_num is not None:
            result["framerate_num"] = self.framerate_num
        if self.framerate_den is not None:
            result["framerate_den"] = self.framerate_den
        if self.interlace_mode:
            result["interlace_mode"] = self.interlace_mode
        if self.colorimetry:
            result["colorimetry"] = self.colorimetry

        return result


@dataclass
class GstMetadata:
    """
    GStreamer metadata structure.

    Matches the JSON structure written by GStreamer plugins.
    Compatible with C# GstMetadata record.
    """

    type: str
    version: str
    caps: GstCaps
    element_name: str

    @classmethod
    def from_json(cls, json_data: str | bytes | dict[str, Any]) -> GstMetadata:
        """
        Create GstMetadata from JSON data.

        Args:
            json_data: JSON string, bytes, or dictionary

        Returns:
            GstMetadata instance

        Raises:
            ValueError: If JSON is invalid or missing required fields
        """
        # Parse JSON if needed
        if isinstance(json_data, (str, bytes)):
            if isinstance(json_data, bytes):
                json_data = json_data.decode("utf-8")
            try:
                data = json.loads(json_data)
            except json.JSONDecodeError as e:
                raise ValueError(f"Invalid JSON: {e}") from e
        else:
            data = json_data

        # Validate required fields
        if not isinstance(data, dict):
            raise ValueError("JSON must be an object/dictionary")

        # Get required fields
        type_str = data.get("type", "")
        version = data.get("version", "")
        element_name = data.get("element_name", "")

        # Parse caps - it's a STRING in the JSON!
        caps_data = data.get("caps")
        if isinstance(caps_data, str):
            # This is the normal case - caps is a string that needs parsing
            caps = GstCaps.parse(caps_data)
        elif isinstance(caps_data, dict):
            # Fallback for dict format (shouldn't happen with real GStreamer)
            # Create a simple caps from dict
            width = caps_data.get("width", 640)
            height = caps_data.get("height", 480)
            format_str = caps_data.get("format", "RGB")
            caps = GstCaps.from_simple(width, height, format_str)
        else:
            raise ValueError(f"Invalid caps data type: {type(caps_data)}")

        return cls(type=type_str, version=version, caps=caps, element_name=element_name)

    def to_json(self) -> str:
        """Convert to JSON string."""
        return json.dumps(self.to_dict())

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary representation."""
        return {
            "type": self.type,
            "version": self.version,
            "caps": str(self.caps),  # Caps as string for C# compatibility
            "element_name": self.element_name,
        }

    def __str__(self) -> str:
        """String representation."""
        return f"GstMetadata(type={self.type}, element={self.element_name}, caps={self.caps})"
