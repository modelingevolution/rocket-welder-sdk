"""
GStreamer metadata structures for RocketWelder SDK.
Matches C# GstCaps and GstMetadata functionality.
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from typing import Any


@dataclass
class GstCaps:
    """
    GStreamer capabilities representation.

    Represents video format capabilities including format, dimensions, framerate, etc.
    """

    format: str | None = None
    width: int | None = None
    height: int | None = None
    framerate: str | None = None  # e.g., "30/1" or "25/1"
    pixel_aspect_ratio: str | None = None  # e.g., "1/1"
    interlace_mode: str | None = None  # e.g., "progressive"
    colorimetry: str | None = None
    chroma_site: str | None = None

    # Additional fields can be stored here
    extra_fields: dict[str, Any] = field(default_factory=dict)

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> GstCaps:
        """
        Create GstCaps from dictionary.

        Args:
            data: Dictionary containing caps data

        Returns:
            GstCaps instance
        """
        # Extract known fields
        caps = cls(
            format=data.get("format"),
            width=data.get("width"),
            height=data.get("height"),
            framerate=data.get("framerate"),
            pixel_aspect_ratio=data.get("pixel-aspect-ratio"),
            interlace_mode=data.get("interlace-mode"),
            colorimetry=data.get("colorimetry"),
            chroma_site=data.get("chroma-site"),
        )

        # Store any extra fields
        known_fields = {
            "format",
            "width",
            "height",
            "framerate",
            "pixel-aspect-ratio",
            "interlace-mode",
            "colorimetry",
            "chroma-site",
        }

        for key, value in data.items():
            if key not in known_fields:
                caps.extra_fields[key] = value

        return caps

    @classmethod
    def from_string(cls, caps_string: str) -> GstCaps:
        """
        Parse GStreamer caps string.

        Args:
            caps_string: GStreamer caps string (e.g., "video/x-raw,format=RGB,width=640,height=480")

        Returns:
            GstCaps instance
        """
        if not caps_string:
            return cls()

        # Remove media type prefix if present
        if "/" in caps_string and "," in caps_string:
            # Has media type prefix (e.g., "video/x-raw,format=RGB")
            _, params = caps_string.split(",", 1)
        else:
            # No media type prefix (e.g., "format=RGB,width=320")
            params = caps_string

        # Parse parameters
        data: dict[str, Any] = {}
        for param in params.split(","):
            if "=" in param:
                key, value = param.split("=", 1)
                key = key.strip()
                value_str = value.strip()

                # Try to parse numeric values
                if value_str.isdigit():
                    data[key] = int(value_str)
                elif key in ["width", "height"] and value_str.startswith("(int)"):
                    data[key] = int(value_str[5:].strip())
                else:
                    data[key] = value_str

        return cls.from_dict(data)

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary representation."""
        result: dict[str, Any] = {}

        if self.format is not None:
            result["format"] = self.format
        if self.width is not None:
            result["width"] = self.width
        if self.height is not None:
            result["height"] = self.height
        if self.framerate is not None:
            result["framerate"] = self.framerate
        if self.pixel_aspect_ratio is not None:
            result["pixel-aspect-ratio"] = self.pixel_aspect_ratio
        if self.interlace_mode is not None:
            result["interlace-mode"] = self.interlace_mode
        if self.colorimetry is not None:
            result["colorimetry"] = self.colorimetry
        if self.chroma_site is not None:
            result["chroma-site"] = self.chroma_site

        # Add extra fields
        result.update(self.extra_fields)

        return result

    def to_string(self) -> str:
        """Convert to GStreamer caps string format."""
        params = []

        if self.format:
            params.append(f"format={self.format}")
        if self.width is not None:
            params.append(f"width={self.width}")
        if self.height is not None:
            params.append(f"height={self.height}")
        if self.framerate:
            params.append(f"framerate={self.framerate}")
        if self.pixel_aspect_ratio:
            params.append(f"pixel-aspect-ratio={self.pixel_aspect_ratio}")
        if self.interlace_mode:
            params.append(f"interlace-mode={self.interlace_mode}")

        # Add extra fields
        for key, value in self.extra_fields.items():
            params.append(f"{key}={value}")

        if params:
            return "video/x-raw," + ",".join(params)
        else:
            return "video/x-raw"

    @property
    def framerate_tuple(self) -> tuple[int, int] | None:
        """Get framerate as a tuple of (numerator, denominator)."""
        if not self.framerate or "/" not in self.framerate:
            return None

        try:
            num, denom = self.framerate.split("/")
            return (int(num), int(denom))
        except (ValueError, AttributeError):
            return None

    @property
    def fps(self) -> float | None:
        """Get framerate as floating-point FPS value."""
        fr = self.framerate_tuple
        if fr and fr[1] != 0:
            return fr[0] / fr[1]
        return None


@dataclass
class GstMetadata:
    """
    GStreamer metadata structure.

    Matches the JSON structure written by GStreamer plugins.
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
        """
        data = json.loads(json_data) if isinstance(json_data, (str, bytes)) else json_data

        # Parse caps
        caps_data = data.get("caps", {})
        if isinstance(caps_data, str):
            caps = GstCaps.from_string(caps_data)
        elif isinstance(caps_data, dict):
            caps = GstCaps.from_dict(caps_data)
        else:
            caps = GstCaps()

        return cls(
            type=data.get("type", ""),
            version=data.get("version", ""),
            caps=caps,
            element_name=data.get("element_name", ""),
        )

    def to_json(self) -> str:
        """Convert to JSON string."""
        return json.dumps(self.to_dict())

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary representation."""
        return {
            "type": self.type,
            "version": self.version,
            "caps": self.caps.to_dict(),
            "element_name": self.element_name,
        }
