"""
Unit tests for BinaryFrameReader, BinaryFrameWriter, and Varint classes.
Matches C# behavior from RocketWelder.SDK.Protocols.
"""

import struct

import pytest

from rocket_welder_sdk.binary_frame_reader import BinaryFrameReader
from rocket_welder_sdk.binary_frame_writer import BinaryFrameWriter
from rocket_welder_sdk.varint import (
    MAX_BYTES_UINT32,
    encode_varint,
    encode_zigzag,
    get_byte_count,
    read_varint,
    read_zigzag,
    try_read_varint,
    write_varint,
    write_zigzag,
    zigzag_decode,
    zigzag_encode,
)


class TestVarint:
    """Test suite for Varint encoding/decoding."""

    def test_zigzag_encode_zero(self) -> None:
        """Test ZigZag encoding of 0."""
        assert zigzag_encode(0) == 0

    def test_zigzag_encode_negative_one(self) -> None:
        """Test ZigZag encoding of -1."""
        assert zigzag_encode(-1) == 1

    def test_zigzag_encode_positive_one(self) -> None:
        """Test ZigZag encoding of 1."""
        assert zigzag_encode(1) == 2

    def test_zigzag_encode_negative_two(self) -> None:
        """Test ZigZag encoding of -2."""
        assert zigzag_encode(-2) == 3

    def test_zigzag_encode_positive_two(self) -> None:
        """Test ZigZag encoding of 2."""
        assert zigzag_encode(2) == 4

    def test_zigzag_decode_zero(self) -> None:
        """Test ZigZag decoding of 0."""
        assert zigzag_decode(0) == 0

    def test_zigzag_decode_one(self) -> None:
        """Test ZigZag decoding of 1 -> -1."""
        assert zigzag_decode(1) == -1

    def test_zigzag_decode_two(self) -> None:
        """Test ZigZag decoding of 2 -> 1."""
        assert zigzag_decode(2) == 1

    def test_zigzag_decode_three(self) -> None:
        """Test ZigZag decoding of 3 -> -2."""
        assert zigzag_decode(3) == -2

    def test_zigzag_roundtrip(self) -> None:
        """Test ZigZag encode/decode roundtrip."""
        for value in [-1000, -100, -1, 0, 1, 100, 1000]:
            encoded = zigzag_encode(value)
            decoded = zigzag_decode(encoded)
            assert decoded == value

    def test_get_byte_count_small(self) -> None:
        """Test byte count for small values."""
        assert get_byte_count(0) == 1
        assert get_byte_count(0x7F) == 1  # 127

    def test_get_byte_count_two_bytes(self) -> None:
        """Test byte count for 2-byte values."""
        assert get_byte_count(0x80) == 2  # 128
        assert get_byte_count(0x3FFF) == 2  # 16383

    def test_get_byte_count_three_bytes(self) -> None:
        """Test byte count for 3-byte values."""
        assert get_byte_count(0x4000) == 3  # 16384
        assert get_byte_count(0x1FFFFF) == 3

    def test_get_byte_count_four_bytes(self) -> None:
        """Test byte count for 4-byte values."""
        assert get_byte_count(0x200000) == 4
        assert get_byte_count(0xFFFFFFF) == 4

    def test_get_byte_count_five_bytes(self) -> None:
        """Test byte count for 5-byte values."""
        assert get_byte_count(0x10000000) == 5
        assert get_byte_count(0xFFFFFFFF) == 5

    def test_write_read_varint_zero(self) -> None:
        """Test write/read roundtrip for 0."""
        buffer = bytearray(MAX_BYTES_UINT32)
        bytes_written = write_varint(buffer, 0, 0)
        assert bytes_written == 1
        value, bytes_read = read_varint(bytes(buffer), 0)
        assert value == 0
        assert bytes_read == 1

    def test_write_read_varint_small(self) -> None:
        """Test write/read roundtrip for small values."""
        for test_value in [1, 100, 127]:
            buffer = bytearray(MAX_BYTES_UINT32)
            bytes_written = write_varint(buffer, 0, test_value)
            value, bytes_read = read_varint(bytes(buffer), 0)
            assert value == test_value
            assert bytes_written == bytes_read

    def test_write_read_varint_large(self) -> None:
        """Test write/read roundtrip for large values."""
        for test_value in [128, 16383, 16384, 2097151, 268435455, 0xFFFFFFFF]:
            buffer = bytearray(MAX_BYTES_UINT32)
            bytes_written = write_varint(buffer, 0, test_value)
            value, bytes_read = read_varint(bytes(buffer), 0)
            assert value == test_value
            assert bytes_written == bytes_read

    def test_write_read_zigzag(self) -> None:
        """Test ZigZag write/read roundtrip."""
        for test_value in [-1000, -100, -1, 0, 1, 100, 1000]:
            buffer = bytearray(MAX_BYTES_UINT32)
            bytes_written = write_zigzag(buffer, 0, test_value)
            value, bytes_read = read_zigzag(bytes(buffer), 0)
            assert value == test_value
            assert bytes_written == bytes_read

    def test_try_read_varint_success(self) -> None:
        """Test try_read_varint with valid data."""
        data = encode_varint(12345)
        success, value, bytes_read = try_read_varint(data, 0)
        assert success is True
        assert value == 12345
        assert bytes_read == len(data)

    def test_try_read_varint_incomplete(self) -> None:
        """Test try_read_varint with incomplete data."""
        # Create incomplete varint (continuation bit set but no more data)
        data = bytes([0x80])  # Continuation bit set
        success, value, bytes_read = try_read_varint(data, 0)
        assert success is False

    def test_read_varint_unexpected_end(self) -> None:
        """Test read_varint with incomplete data raises EOFError."""
        data = bytes([0x80])  # Continuation bit set, no more data
        with pytest.raises(EOFError):
            read_varint(data, 0)

    def test_encode_varint_convenience(self) -> None:
        """Test encode_varint convenience function."""
        encoded = encode_varint(300)
        value, _ = read_varint(encoded, 0)
        assert value == 300

    def test_encode_zigzag_convenience(self) -> None:
        """Test encode_zigzag convenience function."""
        encoded = encode_zigzag(-100)
        value, _ = read_zigzag(encoded, 0)
        assert value == -100


class TestBinaryFrameReader:
    """Test suite for BinaryFrameReader class."""

    def test_read_byte(self) -> None:
        """Test reading a single byte."""
        reader = BinaryFrameReader(bytes([0x42]))
        assert reader.read_byte() == 0x42
        assert reader.position == 1

    def test_read_byte_eof(self) -> None:
        """Test reading byte at end of data raises EOFError."""
        reader = BinaryFrameReader(bytes([]))
        with pytest.raises(EOFError):
            reader.read_byte()

    def test_read_uint64_le(self) -> None:
        """Test reading unsigned 64-bit integer."""
        data = struct.pack("<Q", 0x123456789ABCDEF0)
        reader = BinaryFrameReader(data)
        assert reader.read_uint64_le() == 0x123456789ABCDEF0
        assert reader.position == 8

    def test_read_int32_le(self) -> None:
        """Test reading signed 32-bit integer."""
        data = struct.pack("<i", -12345678)
        reader = BinaryFrameReader(data)
        assert reader.read_int32_le() == -12345678
        assert reader.position == 4

    def test_read_uint16_le(self) -> None:
        """Test reading unsigned 16-bit integer."""
        data = struct.pack("<H", 0xABCD)
        reader = BinaryFrameReader(data)
        assert reader.read_uint16_le() == 0xABCD
        assert reader.position == 2

    def test_read_single_le(self) -> None:
        """Test reading 32-bit float."""
        data = struct.pack("<f", 3.14159)
        reader = BinaryFrameReader(data)
        assert abs(reader.read_single_le() - 3.14159) < 0.0001
        assert reader.position == 4

    def test_read_varint(self) -> None:
        """Test reading varint-encoded value."""
        data = encode_varint(12345)
        reader = BinaryFrameReader(data)
        assert reader.read_varint() == 12345

    def test_read_zigzag_varint(self) -> None:
        """Test reading ZigZag-encoded value."""
        data = encode_zigzag(-12345)
        reader = BinaryFrameReader(data)
        assert reader.read_zigzag_varint() == -12345

    def test_read_string(self) -> None:
        """Test reading UTF-8 string."""
        text = "Hello, World!"
        encoded = text.encode("utf-8")
        reader = BinaryFrameReader(encoded)
        assert reader.read_string(len(encoded)) == text

    def test_read_string_unicode(self) -> None:
        """Test reading UTF-8 string with unicode characters."""
        text = "Hello, \u4e16\u754c!"  # Hello, World! in Chinese
        encoded = text.encode("utf-8")
        reader = BinaryFrameReader(encoded)
        assert reader.read_string(len(encoded)) == text

    def test_skip(self) -> None:
        """Test skipping bytes."""
        reader = BinaryFrameReader(bytes([1, 2, 3, 4, 5]))
        reader.skip(3)
        assert reader.position == 3
        assert reader.read_byte() == 4

    def test_skip_eof(self) -> None:
        """Test skip beyond end raises EOFError."""
        reader = BinaryFrameReader(bytes([1, 2, 3]))
        with pytest.raises(EOFError):
            reader.skip(10)

    def test_read_bytes(self) -> None:
        """Test reading raw bytes."""
        data = bytes([1, 2, 3, 4, 5])
        reader = BinaryFrameReader(data)
        result = reader.read_bytes(3)
        assert result == bytes([1, 2, 3])
        assert reader.position == 3

    def test_has_more(self) -> None:
        """Test has_more property."""
        reader = BinaryFrameReader(bytes([1, 2]))
        assert reader.has_more is True
        reader.read_byte()
        assert reader.has_more is True
        reader.read_byte()
        assert reader.has_more is False

    def test_remaining(self) -> None:
        """Test remaining property."""
        reader = BinaryFrameReader(bytes([1, 2, 3, 4, 5]))
        assert reader.remaining == 5
        reader.read_byte()
        assert reader.remaining == 4
        reader.skip(2)
        assert reader.remaining == 2

    def test_position_setter(self) -> None:
        """Test setting position."""
        reader = BinaryFrameReader(bytes([1, 2, 3, 4, 5]))
        reader.position = 3
        assert reader.read_byte() == 4


class TestBinaryFrameWriter:
    """Test suite for BinaryFrameWriter class."""

    def test_write_byte(self) -> None:
        """Test writing a single byte."""
        writer = BinaryFrameWriter(bytearray(10))
        writer.write_byte(0x42)
        assert writer.position == 1
        assert writer.written_bytes == bytes([0x42])

    def test_write_byte_overflow(self) -> None:
        """Test writing byte to full buffer raises OverflowError."""
        writer = BinaryFrameWriter(bytearray(0))
        with pytest.raises(OverflowError):
            writer.write_byte(0x42)

    def test_write_uint64_le(self) -> None:
        """Test writing unsigned 64-bit integer."""
        writer = BinaryFrameWriter(bytearray(10))
        writer.write_uint64_le(0x123456789ABCDEF0)
        assert writer.position == 8
        expected = struct.pack("<Q", 0x123456789ABCDEF0)
        assert writer.written_bytes == expected

    def test_write_int32_le(self) -> None:
        """Test writing signed 32-bit integer."""
        writer = BinaryFrameWriter(bytearray(10))
        writer.write_int32_le(-12345678)
        assert writer.position == 4
        expected = struct.pack("<i", -12345678)
        assert writer.written_bytes == expected

    def test_write_uint16_le(self) -> None:
        """Test writing unsigned 16-bit integer."""
        writer = BinaryFrameWriter(bytearray(10))
        writer.write_uint16_le(0xABCD)
        assert writer.position == 2
        expected = struct.pack("<H", 0xABCD)
        assert writer.written_bytes == expected

    def test_write_single_le(self) -> None:
        """Test writing 32-bit float."""
        writer = BinaryFrameWriter(bytearray(10))
        writer.write_single_le(3.14159)
        assert writer.position == 4
        # Read back with reader to verify
        reader = BinaryFrameReader(writer.written_bytes)
        assert abs(reader.read_single_le() - 3.14159) < 0.0001

    def test_write_varint(self) -> None:
        """Test writing varint-encoded value."""
        writer = BinaryFrameWriter(bytearray(10))
        writer.write_varint(12345)
        # Read back with reader to verify
        reader = BinaryFrameReader(writer.written_bytes)
        assert reader.read_varint() == 12345

    def test_write_zigzag_varint(self) -> None:
        """Test writing ZigZag-encoded value."""
        writer = BinaryFrameWriter(bytearray(10))
        writer.write_zigzag_varint(-12345)
        # Read back with reader to verify
        reader = BinaryFrameReader(writer.written_bytes)
        assert reader.read_zigzag_varint() == -12345

    def test_write_bytes(self) -> None:
        """Test writing raw bytes."""
        writer = BinaryFrameWriter(bytearray(10))
        writer.write_bytes(bytes([1, 2, 3, 4, 5]))
        assert writer.position == 5
        assert writer.written_bytes == bytes([1, 2, 3, 4, 5])

    def test_write_string(self) -> None:
        """Test writing UTF-8 string."""
        writer = BinaryFrameWriter(bytearray(20))
        writer.write_string("Hello!")
        # Read back with reader to verify
        reader = BinaryFrameReader(writer.written_bytes)
        assert reader.read_string(6) == "Hello!"

    def test_remaining(self) -> None:
        """Test remaining property."""
        writer = BinaryFrameWriter(bytearray(10))
        assert writer.remaining == 10
        writer.write_byte(0x42)
        assert writer.remaining == 9

    def test_reset(self) -> None:
        """Test reset method."""
        writer = BinaryFrameWriter(bytearray(10))
        writer.write_byte(0x42)
        writer.write_byte(0x43)
        assert writer.position == 2
        writer.reset()
        assert writer.position == 0

    def test_with_capacity(self) -> None:
        """Test with_capacity factory method."""
        writer = BinaryFrameWriter.with_capacity(100)
        assert writer.remaining == 100

    def test_written_view(self) -> None:
        """Test written_view property."""
        writer = BinaryFrameWriter(bytearray(10))
        writer.write_bytes(bytes([1, 2, 3]))
        view = writer.written_view
        assert bytes(view) == bytes([1, 2, 3])


class TestReaderWriterRoundtrip:
    """Test round-trip encoding/decoding between Reader and Writer."""

    def test_roundtrip_mixed_types(self) -> None:
        """Test round-trip with mixed data types."""
        writer = BinaryFrameWriter(bytearray(100))
        writer.write_uint64_le(0x123456789ABCDEF0)
        writer.write_int32_le(-999999)
        writer.write_uint16_le(65535)
        writer.write_single_le(2.71828)
        writer.write_varint(300)
        writer.write_zigzag_varint(-150)
        writer.write_byte(0xFF)

        reader = BinaryFrameReader(writer.written_bytes)
        assert reader.read_uint64_le() == 0x123456789ABCDEF0
        assert reader.read_int32_le() == -999999
        assert reader.read_uint16_le() == 65535
        assert abs(reader.read_single_le() - 2.71828) < 0.0001
        assert reader.read_varint() == 300
        assert reader.read_zigzag_varint() == -150
        assert reader.read_byte() == 0xFF
        assert reader.has_more is False

    def test_roundtrip_frame_header(self) -> None:
        """Test round-trip encoding a typical frame header."""
        # Simulate a KeyPoints frame header
        frame_id = 12345678901234
        is_delta = 1
        count = 100

        writer = BinaryFrameWriter(bytearray(50))
        writer.write_uint64_le(frame_id)
        writer.write_byte(is_delta)
        writer.write_varint(count)

        reader = BinaryFrameReader(writer.written_bytes)
        assert reader.read_uint64_le() == frame_id
        assert reader.read_byte() == is_delta
        assert reader.read_varint() == count
