"""
RgbColor - RGBA color representation.

Matches C# RgbColor readonly record struct from BlazorBlaze.VectorGraphics.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import ClassVar


@dataclass(frozen=True)
class RgbColor:
    """
    Represents an RGBA color with byte components.

    This is a frozen dataclass matching the C# `readonly record struct RgbColor`.
    Each component is a byte (0-255).

    Attributes:
        r: Red component (0-255)
        g: Green component (0-255)
        b: Blue component (0-255)
        a: Alpha component (0-255), defaults to 255 (opaque)
    """

    r: int
    g: int
    b: int
    a: int = 255

    # Pre-defined colors (class variables)
    Transparent: ClassVar[RgbColor]
    White: ClassVar[RgbColor]
    Black: ClassVar[RgbColor]
    Red: ClassVar[RgbColor]
    Blue: ClassVar[RgbColor]
    Green: ClassVar[RgbColor]
    Gray: ClassVar[RgbColor]

    def __post_init__(self) -> None:
        """Validate color components are in valid range."""
        if not (0 <= self.r <= 255):
            raise ValueError(f"Red component must be 0-255, got {self.r}")
        if not (0 <= self.g <= 255):
            raise ValueError(f"Green component must be 0-255, got {self.g}")
        if not (0 <= self.b <= 255):
            raise ValueError(f"Blue component must be 0-255, got {self.b}")
        if not (0 <= self.a <= 255):
            raise ValueError(f"Alpha component must be 0-255, got {self.a}")

    def __str__(self) -> str:
        """Returns hex representation (#RRGGBB or #RRGGBBAA)."""
        if self.a == 255:
            return f"#{self.r:02X}{self.g:02X}{self.b:02X}"
        return f"#{self.r:02X}{self.g:02X}{self.b:02X}{self.a:02X}"

    def __repr__(self) -> str:
        """Returns debug representation."""
        return f"RgbColor(r={self.r}, g={self.g}, b={self.b}, a={self.a})"

    def to_bytes(self) -> bytes:
        """Returns RGBA as 4 bytes."""
        return bytes([self.r, self.g, self.b, self.a])

    @classmethod
    def from_bytes(cls, data: bytes, offset: int = 0) -> RgbColor:
        """Creates RgbColor from 4 bytes (RGBA)."""
        return cls(data[offset], data[offset + 1], data[offset + 2], data[offset + 3])

    @classmethod
    def from_hex(cls, hex_str: str) -> RgbColor:
        """
        Creates RgbColor from hex string.

        Supports formats: #RGB, #RGBA, #RRGGBB, #RRGGBBAA
        """
        hex_str = hex_str.lstrip("#")
        if len(hex_str) == 3:
            # #RGB -> #RRGGBB
            hex_str = "".join(c * 2 for c in hex_str)
        elif len(hex_str) == 4:
            # #RGBA -> #RRGGBBAA
            hex_str = "".join(c * 2 for c in hex_str)

        if len(hex_str) == 6:
            r = int(hex_str[0:2], 16)
            g = int(hex_str[2:4], 16)
            b = int(hex_str[4:6], 16)
            return cls(r, g, b)
        elif len(hex_str) == 8:
            r = int(hex_str[0:2], 16)
            g = int(hex_str[2:4], 16)
            b = int(hex_str[4:6], 16)
            a = int(hex_str[6:8], 16)
            return cls(r, g, b, a)
        else:
            raise ValueError(f"Invalid hex color format: #{hex_str}")


# Initialize class-level color constants
RgbColor.Transparent = RgbColor(0, 0, 0, 0)
RgbColor.White = RgbColor(255, 255, 255)
RgbColor.Black = RgbColor(0, 0, 0)
RgbColor.Red = RgbColor(255, 0, 0)
RgbColor.Blue = RgbColor(0, 0, 255)
RgbColor.Green = RgbColor(0, 255, 0)
RgbColor.Gray = RgbColor(128, 128, 128)
