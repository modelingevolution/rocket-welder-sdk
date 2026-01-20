"""
Unit tests for Confidence class.
Matches C# Confidence struct behavior from RocketWelder.SDK.Protocols.
"""

import pytest

from rocket_welder_sdk.confidence import FULL, ZERO, Confidence


class TestConfidence:
    """Test suite for Confidence class."""

    def test_create_from_raw(self) -> None:
        """Test creating Confidence from raw ushort value."""
        c = Confidence(raw=32767)
        assert c.raw == 32767
        assert abs(c.normalized - 0.5) < 0.001  # Approximately 0.5

    def test_create_from_raw_max(self) -> None:
        """Test creating Confidence from max raw value."""
        c = Confidence(raw=65535)
        assert c.raw == 65535
        assert c.normalized == 1.0

    def test_create_from_raw_zero(self) -> None:
        """Test creating Confidence from zero raw value."""
        c = Confidence(raw=0)
        assert c.raw == 0
        assert c.normalized == 0.0

    def test_create_from_raw_invalid_negative(self) -> None:
        """Test creating Confidence from negative value raises error."""
        with pytest.raises(ValueError):
            Confidence(raw=-1)

    def test_create_from_raw_invalid_overflow(self) -> None:
        """Test creating Confidence from overflow value raises error."""
        with pytest.raises(ValueError):
            Confidence(raw=65536)

    def test_from_float_zero(self) -> None:
        """Test creating Confidence from float 0.0."""
        c = Confidence.from_float(0.0)
        assert c.raw == 0
        assert c.normalized == 0.0

    def test_from_float_full(self) -> None:
        """Test creating Confidence from float 1.0."""
        c = Confidence.from_float(1.0)
        assert c.raw == 65535
        assert c.normalized == 1.0

    def test_from_float_half(self) -> None:
        """Test creating Confidence from float 0.5."""
        c = Confidence.from_float(0.5)
        # Should be approximately half of max
        assert abs(c.raw - 32767) <= 1
        assert abs(c.normalized - 0.5) < 0.001

    def test_from_float_clamps_negative(self) -> None:
        """Test that negative float is clamped to 0.0."""
        c = Confidence.from_float(-0.5)
        assert c.raw == 0
        assert c.normalized == 0.0

    def test_from_float_clamps_overflow(self) -> None:
        """Test that overflow float is clamped to 1.0."""
        c = Confidence.from_float(1.5)
        assert c.raw == 65535
        assert c.normalized == 1.0

    def test_full_constant(self) -> None:
        """Test Full constant is 1.0."""
        assert FULL.raw == 65535
        assert FULL.normalized == 1.0
        # Also test class method
        assert Confidence.full().raw == 65535

    def test_zero_constant(self) -> None:
        """Test Zero constant is 0.0."""
        assert ZERO.raw == 0
        assert ZERO.normalized == 0.0
        # Also test class method
        assert Confidence.zero().raw == 0

    def test_float_conversion(self) -> None:
        """Test implicit float conversion."""
        c = Confidence.from_float(0.75)
        assert abs(float(c) - 0.75) < 0.001

    def test_int_conversion(self) -> None:
        """Test explicit int conversion (raw value)."""
        c = Confidence(raw=32767)
        assert int(c) == 32767

    def test_str_format(self) -> None:
        """Test string formatting as percentage."""
        c = Confidence.from_float(0.95)
        s = str(c)
        assert "95" in s
        assert "%" in s

    def test_str_format_zero(self) -> None:
        """Test string formatting for zero."""
        s = str(ZERO)
        assert "0" in s
        assert "%" in s

    def test_str_format_full(self) -> None:
        """Test string formatting for full."""
        s = str(FULL)
        assert "100" in s
        assert "%" in s

    def test_repr(self) -> None:
        """Test repr representation."""
        c = Confidence(raw=32767)
        r = repr(c)
        assert "Confidence" in r
        assert "32767" in r
        assert "0.5" in r  # Approximately

    def test_comparison_confidence_lt(self) -> None:
        """Test less than comparison between Confidence values."""
        low = Confidence.from_float(0.3)
        high = Confidence.from_float(0.9)
        assert low < high
        assert not high < low

    def test_comparison_confidence_gt(self) -> None:
        """Test greater than comparison between Confidence values."""
        low = Confidence.from_float(0.3)
        high = Confidence.from_float(0.9)
        assert high > low
        assert not low > high

    def test_comparison_confidence_le(self) -> None:
        """Test less than or equal comparison between Confidence values."""
        c1 = Confidence.from_float(0.5)
        c2 = Confidence.from_float(0.5)
        assert c1 <= c2
        assert c2 <= c1

    def test_comparison_confidence_ge(self) -> None:
        """Test greater than or equal comparison between Confidence values."""
        c1 = Confidence.from_float(0.5)
        c2 = Confidence.from_float(0.5)
        assert c1 >= c2
        assert c2 >= c1

    def test_comparison_float_lt(self) -> None:
        """Test less than comparison with float (C# operator <)."""
        c = Confidence.from_float(0.5)
        assert c < 0.9
        assert not c < 0.3

    def test_comparison_float_gt(self) -> None:
        """Test greater than comparison with float (C# operator >)."""
        c = Confidence.from_float(0.9)
        assert c > 0.5
        assert not c > 0.95

    def test_comparison_float_le(self) -> None:
        """Test less than or equal comparison with float."""
        c = Confidence.from_float(0.5)
        assert c <= 0.5
        assert c <= 0.9
        assert not c <= 0.3

    def test_comparison_float_ge(self) -> None:
        """Test greater than or equal comparison with float."""
        c = Confidence.from_float(0.5)
        # Note: 0.5 * 65535 = 32767.5, truncates to 32767
        # 32767 / 65535 = 0.49999237... which is slightly < 0.5
        # So we test with a slightly lower threshold
        assert c >= 0.49
        assert c >= 0.3
        assert not c >= 0.9

    def test_equality(self) -> None:
        """Test equality comparison."""
        c1 = Confidence(raw=32767)
        c2 = Confidence(raw=32767)
        c3 = Confidence(raw=65535)
        assert c1 == c2
        assert c1 != c3

    def test_frozen(self) -> None:
        """Test that Confidence is immutable (frozen dataclass)."""
        c = Confidence(raw=1000)
        with pytest.raises(AttributeError):
            c.raw = 2000  # type: ignore[misc]

    def test_hash(self) -> None:
        """Test that Confidence is hashable (can be used in sets/dicts)."""
        c1 = Confidence(raw=32767)
        c2 = Confidence(raw=32767)
        s = {c1, c2}
        assert len(s) == 1  # Same value should hash to same

    def test_parse_float_string(self) -> None:
        """Test parsing float string (0.0-1.0)."""
        c = Confidence.parse("0.95")
        assert abs(c.normalized - 0.95) < 0.001

    def test_parse_percentage_string(self) -> None:
        """Test parsing percentage string."""
        c = Confidence.parse("95.0%")
        assert abs(c.normalized - 0.95) < 0.001

    def test_parse_int_raw(self) -> None:
        """Test parsing integer as raw value."""
        c = Confidence.parse(32767)
        assert c.raw == 32767

    def test_parse_float_value(self) -> None:
        """Test parsing float value."""
        c = Confidence.parse(0.75)
        assert abs(c.normalized - 0.75) < 0.001

    def test_parse_confidence(self) -> None:
        """Test parsing Confidence returns same instance."""
        c1 = Confidence.from_float(0.5)
        c2 = Confidence.parse(c1)
        assert c1 == c2

    def test_parse_empty_string_raises(self) -> None:
        """Test parsing empty string raises ValueError."""
        with pytest.raises(ValueError):
            Confidence.parse("")

    def test_parse_invalid_string_raises(self) -> None:
        """Test parsing invalid string raises ValueError."""
        with pytest.raises(ValueError):
            Confidence.parse("invalid")

    def test_parse_invalid_int_raises(self) -> None:
        """Test parsing integer out of range raises ValueError."""
        with pytest.raises(ValueError):
            Confidence.parse(70000)  # > 65535

    def test_try_parse_valid(self) -> None:
        """Test try_parse returns value on success."""
        c = Confidence.try_parse("0.95")
        assert c is not None
        assert abs(c.normalized - 0.95) < 0.001

    def test_try_parse_invalid(self) -> None:
        """Test try_parse returns None on failure."""
        c = Confidence.try_parse("invalid")
        assert c is None

    def test_try_parse_empty(self) -> None:
        """Test try_parse returns None for empty string."""
        c = Confidence.try_parse("")
        assert c is None
