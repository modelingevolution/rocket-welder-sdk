"""
Zero-allocation binary writer for encoding streaming protocol data.
Matches C# BinaryFrameWriter ref struct from RocketWelder.SDK.Protocols.

Symmetric counterpart to BinaryFrameReader for round-trip testing.
Designed for high-performance frame encoding in real-time video processing.
"""

from __future__ import annotations

import struct
from typing import Union

from .varint import MAX_BYTES_UINT32, get_byte_count, write_varint, write_zigzag


class BinaryFrameWriter:
    """
    Binary writer for encoding streaming protocol data.

    Writes data to a pre-allocated bytearray buffer with position tracking.
    All multi-byte integers are written as little-endian.

    Attributes:
        position: Current write position in the buffer.
    """

    __slots__ = ("_buffer", "_position")

    def __init__(self, buffer: bytearray) -> None:
        """
        Initialize writer with a buffer.

        Args:
            buffer: Destination buffer (bytearray)
        """
        self._buffer = buffer
        self._position = 0

    @classmethod
    def with_capacity(cls, capacity: int) -> BinaryFrameWriter:
        """
        Create a writer with a new buffer of specified capacity.

        Args:
            capacity: Size of buffer to create

        Returns:
            New BinaryFrameWriter instance
        """
        return cls(bytearray(capacity))

    @property
    def position(self) -> int:
        """Current write position in the buffer."""
        return self._position

    @property
    def remaining(self) -> int:
        """Remaining bytes available to write."""
        return len(self._buffer) - self._position

    @property
    def written_bytes(self) -> bytes:
        """Returns the portion of the buffer that has been written to."""
        return bytes(self._buffer[: self._position])

    @property
    def written_view(self) -> memoryview:
        """Returns a memoryview of the written portion of the buffer."""
        return memoryview(self._buffer)[: self._position]

    def write_byte(self, value: int) -> None:
        """
        Write a single byte.

        Args:
            value: Byte value (0-255)

        Raises:
            OverflowError: If buffer is full
        """
        if self._position >= len(self._buffer):
            raise OverflowError("Buffer overflow: not enough space for byte")
        self._buffer[self._position] = value
        self._position += 1

    def write_uint64_le(self, value: int) -> None:
        """
        Write an unsigned 64-bit integer (little-endian).

        Args:
            value: Unsigned 64-bit value

        Raises:
            OverflowError: If not enough space in buffer
        """
        if self._position + 8 > len(self._buffer):
            raise OverflowError("Buffer overflow: not enough space for UInt64")
        struct.pack_into("<Q", self._buffer, self._position, value)
        self._position += 8

    def write_int32_le(self, value: int) -> None:
        """
        Write a signed 32-bit integer (little-endian).

        Args:
            value: Signed 32-bit value

        Raises:
            OverflowError: If not enough space in buffer
        """
        if self._position + 4 > len(self._buffer):
            raise OverflowError("Buffer overflow: not enough space for Int32")
        struct.pack_into("<i", self._buffer, self._position, value)
        self._position += 4

    def write_uint16_le(self, value: int) -> None:
        """
        Write an unsigned 16-bit integer (little-endian).

        Args:
            value: Unsigned 16-bit value

        Raises:
            OverflowError: If not enough space in buffer
        """
        if self._position + 2 > len(self._buffer):
            raise OverflowError("Buffer overflow: not enough space for UInt16")
        struct.pack_into("<H", self._buffer, self._position, value)
        self._position += 2

    def write_single_le(self, value: float) -> None:
        """
        Write a 32-bit floating point (little-endian).

        Args:
            value: 32-bit float value

        Raises:
            OverflowError: If not enough space in buffer
        """
        if self._position + 4 > len(self._buffer):
            raise OverflowError("Buffer overflow: not enough space for Single")
        struct.pack_into("<f", self._buffer, self._position, value)
        self._position += 4

    def write_varint(self, value: int) -> None:
        """
        Write a varint-encoded unsigned 32-bit integer.

        Args:
            value: Unsigned value to encode

        Raises:
            OverflowError: If not enough space in buffer
        """
        # Check if we have worst-case space or exact space needed
        # (intentionally nested for optimization - fast check first)
        if self._position + MAX_BYTES_UINT32 > len(self._buffer):  # noqa: SIM102
            if self._position + get_byte_count(value) > len(self._buffer):
                raise OverflowError("Buffer overflow: not enough space for varint")

        bytes_written = write_varint(self._buffer, self._position, value)
        self._position += bytes_written

    def write_zigzag_varint(self, value: int) -> None:
        """
        Write a ZigZag-encoded signed integer (varint format).

        Args:
            value: Signed value to encode

        Raises:
            OverflowError: If not enough space in buffer
        """
        if self._position + MAX_BYTES_UINT32 > len(self._buffer):
            raise OverflowError("Buffer overflow: not enough space for varint")

        bytes_written = write_zigzag(self._buffer, self._position, value)
        self._position += bytes_written

    def write_bytes(self, source: Union[bytes, bytearray, memoryview]) -> None:
        """
        Write raw bytes from a bytes-like object.

        Args:
            source: Source bytes to write

        Raises:
            OverflowError: If not enough space in buffer
        """
        if self._position + len(source) > len(self._buffer):
            raise OverflowError(f"Buffer overflow: not enough space for {len(source)} bytes")
        self._buffer[self._position : self._position + len(source)] = source
        self._position += len(source)

    def write_string(self, value: str) -> None:
        """
        Write a UTF-8 encoded string.

        Args:
            value: String to encode and write

        Raises:
            OverflowError: If not enough space in buffer
        """
        encoded = value.encode("utf-8")
        self.write_bytes(encoded)

    def reset(self) -> None:
        """Reset position to beginning of buffer."""
        self._position = 0
