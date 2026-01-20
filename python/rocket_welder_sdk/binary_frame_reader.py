"""
Zero-allocation binary reader for parsing streaming protocol data.
Matches C# BinaryFrameReader ref struct from RocketWelder.SDK.Protocols.

Designed for high-performance frame decoding in real-time video processing.
"""

from __future__ import annotations

import struct
from typing import Union

from .varint import read_varint, read_zigzag


class BinaryFrameReader:
    """
    Binary reader for parsing streaming protocol data.

    Reads data from a bytes-like object with position tracking.
    All multi-byte integers are read as little-endian.

    Attributes:
        position: Current read position in the buffer.
    """

    __slots__ = ("_data", "_position")

    def __init__(self, data: Union[bytes, bytearray, memoryview]) -> None:
        """
        Initialize reader with data buffer.

        Args:
            data: Source data buffer (bytes, bytearray, or memoryview)
        """
        if isinstance(data, memoryview):
            self._data = bytes(data)
        else:
            self._data = bytes(data)
        self._position = 0

    @property
    def has_more(self) -> bool:
        """Returns True if there is more data to read."""
        return self._position < len(self._data)

    @property
    def position(self) -> int:
        """Current read position in the buffer."""
        return self._position

    @position.setter
    def position(self, value: int) -> None:
        """Set the current read position."""
        self._position = value

    @property
    def remaining(self) -> int:
        """Remaining bytes available to read."""
        return len(self._data) - self._position

    def read_byte(self) -> int:
        """
        Read a single byte.

        Returns:
            The byte value (0-255)

        Raises:
            EOFError: If no more data available
        """
        if self._position >= len(self._data):
            raise EOFError("Unexpected end of data")
        value = self._data[self._position]
        self._position += 1
        return value

    def read_uint64_le(self) -> int:
        """
        Read an unsigned 64-bit integer (little-endian).

        Returns:
            The unsigned 64-bit value

        Raises:
            EOFError: If not enough data available
        """
        if self._position + 8 > len(self._data):
            raise EOFError("Not enough data for UInt64")
        (value,) = struct.unpack_from("<Q", self._data, self._position)
        self._position += 8
        return int(value)

    def read_int32_le(self) -> int:
        """
        Read a signed 32-bit integer (little-endian).

        Returns:
            The signed 32-bit value

        Raises:
            EOFError: If not enough data available
        """
        if self._position + 4 > len(self._data):
            raise EOFError("Not enough data for Int32")
        (value,) = struct.unpack_from("<i", self._data, self._position)
        self._position += 4
        return int(value)

    def read_uint16_le(self) -> int:
        """
        Read an unsigned 16-bit integer (little-endian).

        Returns:
            The unsigned 16-bit value

        Raises:
            EOFError: If not enough data available
        """
        if self._position + 2 > len(self._data):
            raise EOFError("Not enough data for UInt16")
        (value,) = struct.unpack_from("<H", self._data, self._position)
        self._position += 2
        return int(value)

    def read_single_le(self) -> float:
        """
        Read a 32-bit floating point (little-endian).

        Returns:
            The 32-bit float value

        Raises:
            EOFError: If not enough data available
        """
        if self._position + 4 > len(self._data):
            raise EOFError("Not enough data for Single")
        (value,) = struct.unpack_from("<f", self._data, self._position)
        self._position += 4
        return float(value)

    def read_varint(self) -> int:
        """
        Read a varint-encoded unsigned 32-bit integer.

        Returns:
            The decoded unsigned value

        Raises:
            EOFError: If buffer ends before varint completes
            ValueError: If varint is malformed
        """
        value, bytes_read = read_varint(self._data, self._position)
        self._position += bytes_read
        return value

    def read_zigzag_varint(self) -> int:
        """
        Read a ZigZag-encoded signed integer (varint format).

        Returns:
            The decoded signed value

        Raises:
            EOFError: If buffer ends before varint completes
            ValueError: If varint is malformed
        """
        value, bytes_read = read_zigzag(self._data, self._position)
        self._position += bytes_read
        return value

    def read_string(self, length: int) -> str:
        """
        Read a UTF-8 encoded string of specified length.

        Args:
            length: Number of bytes to read

        Returns:
            The decoded UTF-8 string

        Raises:
            EOFError: If not enough data available
        """
        if self._position + length > len(self._data):
            raise EOFError(f"Not enough data for string of length {length}")
        value = self._data[self._position : self._position + length].decode("utf-8")
        self._position += length
        return value

    def skip(self, count: int) -> None:
        """
        Skip a specified number of bytes.

        Args:
            count: Number of bytes to skip

        Raises:
            EOFError: If not enough data available
        """
        if self._position + count > len(self._data):
            raise EOFError(f"Cannot skip {count} bytes, only {self.remaining} remaining")
        self._position += count

    def read_bytes(self, length: int) -> bytes:
        """
        Read raw bytes.

        Args:
            length: Number of bytes to read

        Returns:
            The raw bytes

        Raises:
            EOFError: If not enough data available
        """
        if self._position + length > len(self._data):
            raise EOFError(f"Not enough data for {length} bytes")
        value = self._data[self._position : self._position + length]
        self._position += length
        return value
