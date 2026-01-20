"""
Confidence value type for ML detection results.
Matches C# Confidence struct from RocketWelder.SDK.Protocols.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import ClassVar, Optional, Union


@dataclass(frozen=True)
class Confidence:
    """
    Represents a confidence score for ML detection results.

    Internally stored as ushort (0-65535) for full precision.
    Provides normalized float (0.0-1.0) for easy usage.

    Examples:
        >>> # Create from float (0.0-1.0)
        >>> c1 = Confidence.from_float(0.95)
        >>>
        >>> # Create from raw ushort (0-65535)
        >>> c2 = Confidence(raw=65535)
        >>>
        >>> # Get normalized float
        >>> normalized = c1.normalized  # 0.95
        >>>
        >>> # Compare with float
        >>> if c1 > 0.9:
        ...     print("High confidence")
        >>>
        >>> # Get raw ushort value
        >>> raw_value = c1.raw
    """

    MAX_RAW: ClassVar[int] = 65535  # ushort.MaxValue

    raw: int
    """The raw ushort value (0-65535)."""

    def __post_init__(self) -> None:
        """Validate raw value is in valid range."""
        if not 0 <= self.raw <= Confidence.MAX_RAW:
            raise ValueError(
                f"Raw value must be between 0 and {Confidence.MAX_RAW}, got {self.raw}"
            )

    @property
    def normalized(self) -> float:
        """Get the normalized float value (0.0-1.0)."""
        return self.raw / Confidence.MAX_RAW

    @classmethod
    def from_float(cls, value: float) -> Confidence:
        """
        Create Confidence from a float value (0.0-1.0).

        The value is clamped to [0.0, 1.0] range before conversion.

        Args:
            value: Float value in range 0.0 to 1.0

        Returns:
            Confidence instance
        """
        clamped = max(0.0, min(1.0, value))
        raw = int(clamped * cls.MAX_RAW)
        return cls(raw=raw)

    @classmethod
    def full(cls) -> Confidence:
        """Full confidence (1.0)."""
        return cls(raw=cls.MAX_RAW)

    @classmethod
    def zero(cls) -> Confidence:
        """Zero confidence (0.0)."""
        return cls(raw=0)

    @classmethod
    def parse(cls, value: Union[str, int, float, Confidence]) -> Confidence:
        """
        Parse a string, number, or Confidence into a Confidence object.

        Equivalent to C# IParsable<Confidence>.Parse().

        Args:
            value: Value to parse (e.g., "0.95", 0.95, 65535)

        Returns:
            Confidence instance

        Raises:
            ValueError: If the value cannot be parsed
        """
        if isinstance(value, Confidence):
            return value

        if isinstance(value, int):
            if 0 <= value <= cls.MAX_RAW:
                return cls(raw=value)
            raise ValueError(f"Integer value must be between 0 and {cls.MAX_RAW}, got {value}")

        if isinstance(value, float):
            return cls.from_float(value)

        if isinstance(value, str):
            value = value.strip()
            if not value:
                raise ValueError("Cannot parse empty string as Confidence")

            # Try parsing as percentage (e.g., "95.0%")
            if value.endswith("%"):
                try:
                    percent = float(value[:-1])
                    return cls.from_float(percent / 100.0)
                except ValueError as e:
                    raise ValueError(f"Invalid percentage format: '{value}'") from e

            # Try parsing as float (0.0-1.0)
            try:
                float_val = float(value)
                if 0.0 <= float_val <= 1.0:
                    return cls.from_float(float_val)
                # If > 1.0, treat as raw ushort value
                if float_val == int(float_val) and 0 <= float_val <= cls.MAX_RAW:
                    return cls(raw=int(float_val))
                raise ValueError(
                    f"Float value must be 0.0-1.0 or integer 0-{cls.MAX_RAW}, got {float_val}"
                )
            except ValueError:
                raise ValueError(f"Invalid Confidence format: '{value}'") from None

        raise ValueError(f"Cannot parse {type(value).__name__} as Confidence")

    @classmethod
    def try_parse(cls, value: Union[str, int, float, Confidence]) -> Optional[Confidence]:
        """
        Try to parse a value into Confidence, returning None on failure.

        Args:
            value: Value to parse

        Returns:
            Confidence instance or None if parsing failed
        """
        try:
            return cls.parse(value)
        except (ValueError, TypeError):
            return None

    def __float__(self) -> float:
        """Convert to float (normalized value)."""
        return self.normalized

    def __int__(self) -> int:
        """Convert to int (raw value)."""
        return self.raw

    def __str__(self) -> str:
        """Return the normalized value as a percentage string (e.g., '95.0%')."""
        return f"{self.normalized:.1%}"

    def __repr__(self) -> str:
        """Developer-friendly representation."""
        return f"Confidence(raw={self.raw}, normalized={self.normalized:.4f})"

    # Comparison operators with float
    def __lt__(self, other: object) -> bool:
        """Less than comparison."""
        if isinstance(other, Confidence):
            return self.raw < other.raw
        if isinstance(other, (int, float)):
            return self.normalized < other
        return NotImplemented

    def __le__(self, other: object) -> bool:
        """Less than or equal comparison."""
        if isinstance(other, Confidence):
            return self.raw <= other.raw
        if isinstance(other, (int, float)):
            return self.normalized <= other
        return NotImplemented

    def __gt__(self, other: object) -> bool:
        """Greater than comparison."""
        if isinstance(other, Confidence):
            return self.raw > other.raw
        if isinstance(other, (int, float)):
            return self.normalized > other
        return NotImplemented

    def __ge__(self, other: object) -> bool:
        """Greater than or equal comparison."""
        if isinstance(other, Confidence):
            return self.raw >= other.raw
        if isinstance(other, (int, float)):
            return self.normalized >= other
        return NotImplemented


# Convenience constants matching C# static properties
FULL = Confidence.full()
ZERO = Confidence.zero()
