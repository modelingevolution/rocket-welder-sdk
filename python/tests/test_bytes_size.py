"""
Enterprise-grade unit tests for BytesSize class.
"""

import pytest

from rocket_welder_sdk import BytesSize


class TestBytesSize:
    """Test suite for BytesSize class."""

    def test_create_from_int(self) -> None:
        """Test creating BytesSize from integer."""
        size = BytesSize(1024)
        assert size.value == 1024
        assert int(size) == 1024
        assert str(size) == "1KB"

    def test_create_with_precision(self) -> None:
        """Test creating BytesSize with precision."""
        size = BytesSize(1536, precision=2)
        assert size.value == 1536
        assert str(size) == "1.50KB"

    def test_parse_simple_values(self) -> None:
        """Test parsing simple byte values."""
        assert BytesSize.parse("0").value == 0
        assert BytesSize.parse("1024").value == 1024
        assert BytesSize.parse("1024B").value == 1024

    def test_parse_kb_values(self) -> None:
        """Test parsing kilobyte values."""
        assert BytesSize.parse("1KB").value == 1024
        assert BytesSize.parse("1K").value == 1024
        assert BytesSize.parse("2KB").value == 2048
        assert BytesSize.parse("1.5KB").value == 1536

    def test_parse_mb_values(self) -> None:
        """Test parsing megabyte values."""
        assert BytesSize.parse("1MB").value == 1024 * 1024
        assert BytesSize.parse("1M").value == 1024 * 1024
        assert BytesSize.parse("256MB").value == 256 * 1024 * 1024
        assert BytesSize.parse("1.5MB").value == int(1.5 * 1024 * 1024)

    def test_parse_gb_values(self) -> None:
        """Test parsing gigabyte values."""
        assert BytesSize.parse("1GB").value == 1024 * 1024 * 1024
        assert BytesSize.parse("1G").value == 1024 * 1024 * 1024
        assert BytesSize.parse("2.5GB").value == int(2.5 * 1024 * 1024 * 1024)

    def test_parse_tb_values(self) -> None:
        """Test parsing terabyte values."""
        assert BytesSize.parse("1TB").value == 1024 * 1024 * 1024 * 1024
        assert BytesSize.parse("1T").value == 1024 * 1024 * 1024 * 1024

    def test_parse_with_spaces(self) -> None:
        """Test parsing values with spaces."""
        assert BytesSize.parse(" 256 MB ").value == 256 * 1024 * 1024
        assert BytesSize.parse("1 KB").value == 1024

    def test_parse_case_insensitive(self) -> None:
        """Test case-insensitive parsing."""
        assert BytesSize.parse("1kb").value == 1024
        assert BytesSize.parse("1Kb").value == 1024
        assert BytesSize.parse("1KB").value == 1024
        assert BytesSize.parse("1mb").value == 1024 * 1024

    def test_parse_decimal_values(self) -> None:
        """Test parsing decimal values."""
        assert BytesSize.parse("1.5KB").value == 1536
        assert BytesSize.parse("2.5MB").value == int(2.5 * 1024 * 1024)
        assert BytesSize.parse("0.5GB").value == 512 * 1024 * 1024

    def test_parse_invalid_values(self) -> None:
        """Test parsing invalid values raises exceptions."""
        with pytest.raises(ValueError):
            BytesSize.parse("")
        with pytest.raises(ValueError):
            BytesSize.parse("invalid")
        with pytest.raises(ValueError):
            BytesSize.parse("1XB")
        with pytest.raises(ValueError):
            BytesSize.parse("abc123")

    def test_try_parse(self) -> None:
        """Test try_parse method."""
        assert BytesSize.try_parse("256MB") is not None
        assert BytesSize.try_parse("256MB").value == 256 * 1024 * 1024
        assert BytesSize.try_parse("invalid") is None
        assert BytesSize.try_parse("") is None

    def test_equality(self) -> None:
        """Test equality comparison."""
        size1 = BytesSize(1024)
        size2 = BytesSize(1024)
        size3 = BytesSize(2048)

        assert size1 == size2
        assert size1 != size3
        assert size1 == 1024
        assert size1 != 2048

    def test_comparison(self) -> None:
        """Test comparison operators."""
        small = BytesSize(1024)
        medium = BytesSize(2048)
        large = BytesSize(4096)

        assert small < medium
        assert medium < large
        assert large > medium
        assert medium > small
        assert small <= medium
        assert medium <= medium
        assert large >= medium
        assert medium >= medium

    def test_arithmetic(self) -> None:
        """Test arithmetic operations."""
        size1 = BytesSize(1024)
        size2 = BytesSize(2048)

        # Addition
        result = size1 + size2
        assert result.value == 3072

        result = size1 + 512
        assert result.value == 1536

        # Subtraction
        result = size2 - size1
        assert result.value == 1024

        result = size2 - 1024
        assert result.value == 1024

    def test_string_formatting(self) -> None:
        """Test string formatting."""
        assert str(BytesSize(0)) == "0B"
        assert str(BytesSize(512)) == "512B"
        assert str(BytesSize(1024)) == "1KB"
        assert str(BytesSize(1536)) == "1.5KB"
        assert str(BytesSize(1024 * 1024)) == "1MB"
        assert str(BytesSize(1024 * 1024 * 1024)) == "1GB"
        assert str(BytesSize(1024 * 1024 * 1024 * 1024)) == "1TB"

    def test_repr(self) -> None:
        """Test repr representation."""
        size = BytesSize(1024, precision=2)
        assert repr(size) == "BytesSize(1024, precision=2)"

    def test_conversions(self) -> None:
        """Test type conversions."""
        size = BytesSize(1536)

        # To int
        assert int(size) == 1536

        # To float
        assert float(size) == 1536.0

    def test_common_sizes(self) -> None:
        """Test common size values."""
        # Common buffer sizes
        assert BytesSize.parse("4KB").value == 4 * 1024
        assert BytesSize.parse("64KB").value == 64 * 1024
        assert BytesSize.parse("256MB").value == 256 * 1024 * 1024
        assert BytesSize.parse("1GB").value == 1024 * 1024 * 1024
