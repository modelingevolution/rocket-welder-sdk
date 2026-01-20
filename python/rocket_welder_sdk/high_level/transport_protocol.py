"""
Unified transport protocol as a value type.

Supports: file://, socket://

Examples:
    file:///home/user/output.bin   - absolute file path
    socket:///tmp/my.sock          - Unix domain socket
"""

from __future__ import annotations

from enum import Enum, auto
from typing import ClassVar, Dict, Optional


class TransportKind(Enum):
    """Transport kind enumeration."""

    FILE = auto()
    """File output."""

    SOCKET = auto()
    """Unix domain socket (direct, no messaging library)."""


class TransportProtocol:
    """
    Unified transport protocol specification as a value type.

    Supports: file://, socket://
    """

    # Predefined protocols
    File: TransportProtocol
    Socket: TransportProtocol

    _SCHEMA_MAP: ClassVar[Dict[str, TransportKind]] = {
        "file": TransportKind.FILE,
        "socket": TransportKind.SOCKET,
    }

    _KIND_TO_SCHEMA: ClassVar[Dict[TransportKind, str]] = {}

    def __init__(self, kind: TransportKind, schema: str) -> None:
        self._kind = kind
        self._schema = schema

    @property
    def kind(self) -> TransportKind:
        """The transport kind."""
        return self._kind

    @property
    def schema(self) -> str:
        """The schema string (e.g., 'file', 'socket')."""
        return self._schema

    # Classification properties

    @property
    def is_file(self) -> bool:
        """True if this is a file transport."""
        return self._kind == TransportKind.FILE

    @property
    def is_socket(self) -> bool:
        """True if this is a Unix socket transport."""
        return self._kind == TransportKind.SOCKET

    def __str__(self) -> str:
        return self._schema

    def __repr__(self) -> str:
        return f"TransportProtocol({self._kind.name}, '{self._schema}')"

    def __eq__(self, other: object) -> bool:
        if isinstance(other, TransportProtocol):
            return self._kind == other._kind
        return False

    def __hash__(self) -> int:
        return hash(self._kind)

    @classmethod
    def parse(cls, s: str) -> TransportProtocol:
        """Parse a protocol string (e.g., 'file', 'socket')."""
        result = cls.try_parse(s)
        if result is None:
            raise ValueError(f"Invalid transport protocol: {s}")
        return result

    @classmethod
    def try_parse(cls, s: Optional[str]) -> Optional[TransportProtocol]:
        """Try to parse a protocol string."""
        if not s:
            return None

        schema = s.lower().strip()
        kind = cls._SCHEMA_MAP.get(schema)
        if kind is None:
            return None

        return cls(kind, schema)


# Initialize predefined protocols
TransportProtocol.File = TransportProtocol(TransportKind.FILE, "file")
TransportProtocol.Socket = TransportProtocol(TransportKind.SOCKET, "socket")

# Initialize reverse lookup map
TransportProtocol._KIND_TO_SCHEMA = {v: k for k, v in TransportProtocol._SCHEMA_MAP.items()}
