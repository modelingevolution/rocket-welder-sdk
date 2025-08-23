"""
Enterprise-grade Bytes size representation with parsing support.
Matches C# Bytes struct functionality.
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from typing import ClassVar


@dataclass(frozen=True)
class BytesSize:
    """
    Immutable representation of byte sizes with human-readable formatting.

    Supports parsing from strings like "256MB", "4KB", "1.5GB" etc.
    """

    _value: int
    _precision: int = 0

    def __init__(self, value: int, precision: int = 0) -> None:
        """Initialize BytesSize with value and optional precision."""
        object.__setattr__(self, "_value", value)
        object.__setattr__(self, "_precision", precision)

    # Size multipliers
    _SUFFIXES: ClassVar[dict[str, int]] = {
        "B": 1,
        "K": 1024,
        "KB": 1024,
        "M": 1024 * 1024,
        "MB": 1024 * 1024,
        "G": 1024 * 1024 * 1024,
        "GB": 1024 * 1024 * 1024,
        "T": 1024 * 1024 * 1024 * 1024,
        "TB": 1024 * 1024 * 1024 * 1024,
        "P": 1024 * 1024 * 1024 * 1024 * 1024,
        "PB": 1024 * 1024 * 1024 * 1024 * 1024,
        "E": 1024 * 1024 * 1024 * 1024 * 1024 * 1024,
        "EB": 1024 * 1024 * 1024 * 1024 * 1024 * 1024,
    }

    # Pattern for parsing size strings
    _PATTERN: ClassVar[re.Pattern[str]] = re.compile(r"^([\d.,]+)\s*([KMGTPE]?B?)$", re.IGNORECASE)

    @property
    def value(self) -> int:
        """Get the raw byte value."""
        return self._value

    def __int__(self) -> int:
        """Convert to integer."""
        return self._value

    def __float__(self) -> float:
        """Convert to float."""
        return float(self._value)

    def __str__(self) -> str:
        """Format as human-readable string."""
        return self._format_size(self._value, self._precision)

    def __repr__(self) -> str:
        """Developer-friendly representation."""
        return f"BytesSize({self._value}, precision={self._precision})"

    def __eq__(self, other: object) -> bool:
        """Equality comparison."""
        if isinstance(other, BytesSize):
            return self._value == other._value
        if isinstance(other, (int, float)):
            return self._value == other
        return False

    def __lt__(self, other: BytesSize | int | float) -> bool:
        """Less than comparison."""
        if isinstance(other, BytesSize):
            return self._value < other._value
        if isinstance(other, (int, float)):
            return self._value < other
        return NotImplemented

    def __le__(self, other: BytesSize | int | float) -> bool:
        """Less than or equal comparison."""
        if isinstance(other, BytesSize):
            return self._value <= other._value
        if isinstance(other, (int, float)):
            return self._value <= other
        return NotImplemented

    def __gt__(self, other: BytesSize | int | float) -> bool:
        """Greater than comparison."""
        if isinstance(other, BytesSize):
            return self._value > other._value
        if isinstance(other, (int, float)):
            return self._value > other
        return NotImplemented

    def __ge__(self, other: BytesSize | int | float) -> bool:
        """Greater than or equal comparison."""
        if isinstance(other, BytesSize):
            return self._value >= other._value
        if isinstance(other, (int, float)):
            return self._value >= other
        return NotImplemented

    def __add__(self, other: BytesSize | int) -> BytesSize:
        """Add byte sizes."""
        if isinstance(other, BytesSize):
            return BytesSize(self._value + other._value, self._precision)
        if isinstance(other, int):
            return BytesSize(self._value + other, self._precision)
        return NotImplemented

    def __sub__(self, other: BytesSize | int) -> BytesSize:
        """Subtract byte sizes."""
        if isinstance(other, BytesSize):
            return BytesSize(self._value - other._value, self._precision)
        if isinstance(other, int):
            return BytesSize(self._value - other, self._precision)
        return NotImplemented

    @classmethod
    def parse(cls, value: str | int | float | BytesSize) -> BytesSize:
        """
        Parse a string, number, or BytesSize into a BytesSize object.

        Args:
            value: Value to parse (e.g., "256MB", 1024, "4.5GB")

        Returns:
            BytesSize instance

        Raises:
            ValueError: If the value cannot be parsed
        """
        if isinstance(value, BytesSize):
            return value

        if isinstance(value, (int, float)):
            return cls(int(value))

        if not isinstance(value, str):
            raise ValueError(f"Cannot parse {type(value).__name__} as BytesSize")

        # Clean the string
        value = value.strip()
        if not value:
            raise ValueError("Cannot parse empty string as BytesSize")

        # Try to match the pattern
        match = cls._PATTERN.match(value)
        if not match:
            raise ValueError(f"Invalid byte size format: '{value}'")

        number_str, suffix = match.groups()

        # Parse the number part (handle different locales)
        number_str = number_str.replace(",", "")  # Remove thousands separators
        try:
            number = float(number_str)
        except ValueError as e:
            raise ValueError(f"Invalid number in byte size: '{number_str}'") from e

        # Get the multiplier
        suffix = suffix.upper() if suffix else "B"
        multiplier = cls._SUFFIXES.get(suffix, 0)

        if multiplier == 0:
            raise ValueError(f"Unknown size suffix: '{suffix}'")

        # Calculate the bytes
        bytes_value = int(number * multiplier)
        return cls(bytes_value)

    @classmethod
    def try_parse(cls, value: str | int | float | BytesSize) -> BytesSize | None:
        """
        Try to parse a value into BytesSize, returning None on failure.

        Args:
            value: Value to parse

        Returns:
            BytesSize instance or None if parsing failed
        """
        try:
            return cls.parse(value)
        except (ValueError, TypeError):
            return None

    @staticmethod
    def _format_size(bytes_value: int, precision: int = 0) -> str:
        """
        Format bytes as human-readable string.

        Args:
            bytes_value: Number of bytes
            precision: Decimal precision for formatting

        Returns:
            Formatted string (e.g., "256MB", "1.5GB")
        """
        if bytes_value == 0:
            return "0B"

        # Determine the appropriate unit
        units = ["B", "KB", "MB", "GB", "TB", "PB", "EB"]
        unit_index = 0
        size = float(bytes_value)

        while size >= 1024 and unit_index < len(units) - 1:
            size /= 1024
            unit_index += 1

        # Format based on precision
        if precision > 0:
            return f"{size:.{precision}f}{units[unit_index]}"
        elif size == int(size):
            return f"{int(size)}{units[unit_index]}"
        else:
            # Auto precision (up to 2 decimal places, remove trailing zeros)
            return f"{size:.2f}".rstrip("0").rstrip(".") + units[unit_index]


# Convenience constants
ZERO = BytesSize(0)
KB = BytesSize(1024)
MB = BytesSize(1024 * 1024)
GB = BytesSize(1024 * 1024 * 1024)
TB = BytesSize(1024 * 1024 * 1024 * 1024)
