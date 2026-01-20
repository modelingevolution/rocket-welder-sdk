"""
Core varint and ZigZag encoding/decoding algorithms.
Matches C# Varint static class from RocketWelder.SDK.Protocols.

Single source of truth for all varint operations in the SDK.
Compatible with Protocol Buffers varint encoding.

Varint encoding uses 7 bits per byte with MSB as continuation flag.
ZigZag encoding maps signed integers to unsigned for efficient varint encoding
of values near zero (both positive and negative).
"""

from __future__ import annotations

from typing import Tuple

# Maximum bytes needed for a uint32 varint
MAX_BYTES_UINT32 = 5


def zigzag_encode(value: int) -> int:
    """
    ZigZag encode a signed integer to unsigned.

    Maps negative numbers to odd positives: 0->0, -1->1, 1->2, -2->3, 2->4, etc.
    This allows efficient varint encoding of signed values near zero.

    Args:
        value: Signed integer to encode

    Returns:
        Unsigned integer (ZigZag encoded)
    """
    return (value << 1) ^ (value >> 31)


def zigzag_decode(value: int) -> int:
    """
    ZigZag decode an unsigned integer to signed.

    Reverses the ZigZag encoding: 0->0, 1->-1, 2->1, 3->-2, 4->2, etc.

    Args:
        value: Unsigned integer (ZigZag encoded)

    Returns:
        Signed integer (decoded)
    """
    return (value >> 1) ^ -(value & 1)


def get_byte_count(value: int) -> int:
    """
    Calculate the number of bytes needed to encode a value as varint.

    Args:
        value: Unsigned integer value

    Returns:
        Number of bytes needed (1-5)
    """
    if value < 0x80:
        return 1
    if value < 0x4000:
        return 2
    if value < 0x200000:
        return 3
    if value < 0x10000000:
        return 4
    return 5


def write_varint(buffer: bytearray, offset: int, value: int) -> int:
    """
    Write a varint to a buffer at the given offset.

    Args:
        buffer: Destination buffer (must have at least 5 bytes from offset)
        offset: Starting offset in buffer
        value: Unsigned value to encode

    Returns:
        Number of bytes written (1-5)
    """
    i = 0
    while value >= 0x80:
        buffer[offset + i] = (value & 0x7F) | 0x80
        value >>= 7
        i += 1
    buffer[offset + i] = value
    return i + 1


def write_zigzag(buffer: bytearray, offset: int, value: int) -> int:
    """
    Write a ZigZag-encoded signed integer as varint.

    Args:
        buffer: Destination buffer (must have at least 5 bytes from offset)
        offset: Starting offset in buffer
        value: Signed value to encode

    Returns:
        Number of bytes written (1-5)
    """
    return write_varint(buffer, offset, zigzag_encode(value))


def read_varint(data: bytes, offset: int) -> Tuple[int, int]:
    """
    Read a varint from bytes at the given offset.

    Args:
        data: Source buffer
        offset: Starting offset in buffer

    Returns:
        Tuple of (decoded_value, bytes_read)

    Raises:
        EOFError: If buffer ends before varint completes
        ValueError: If varint is malformed (too long)
    """
    result = 0
    shift = 0
    i = 0

    while True:
        if offset + i >= len(data):
            raise EOFError("Unexpected end of varint")

        b = data[offset + i]
        result |= (b & 0x7F) << shift
        i += 1

        if (b & 0x80) == 0:
            break

        shift += 7
        if shift >= 35:
            raise ValueError("Varint too long (corrupted data)")

    return result, i


def read_zigzag(data: bytes, offset: int) -> Tuple[int, int]:
    """
    Read a ZigZag-encoded signed integer.

    Args:
        data: Source buffer
        offset: Starting offset in buffer

    Returns:
        Tuple of (decoded_signed_value, bytes_read)
    """
    encoded, bytes_read = read_varint(data, offset)
    return zigzag_decode(encoded), bytes_read


def try_read_varint(data: bytes, offset: int) -> Tuple[bool, int, int]:
    """
    Try to read a varint, returning False if not enough data.

    Args:
        data: Source buffer
        offset: Starting offset in buffer

    Returns:
        Tuple of (success, value, bytes_read)
        If success is False, value and bytes_read are 0
    """
    result = 0
    shift = 0

    for i in range(min(5, len(data) - offset)):
        b = data[offset + i]
        result |= (b & 0x7F) << shift

        if (b & 0x80) == 0:
            return True, result, i + 1

        shift += 7

    return False, 0, 0


def encode_varint(value: int) -> bytes:
    """
    Convenience function to encode a value to varint bytes.

    Args:
        value: Unsigned value to encode

    Returns:
        Bytes containing the varint encoding
    """
    buffer = bytearray(MAX_BYTES_UINT32)
    length = write_varint(buffer, 0, value)
    return bytes(buffer[:length])


def encode_zigzag(value: int) -> bytes:
    """
    Convenience function to encode a signed value to ZigZag varint bytes.

    Args:
        value: Signed value to encode

    Returns:
        Bytes containing the ZigZag varint encoding
    """
    return encode_varint(zigzag_encode(value))
