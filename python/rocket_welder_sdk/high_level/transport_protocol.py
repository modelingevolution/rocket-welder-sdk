"""
Unified transport protocol as a value type.

Supports: file://, socket://, nng+push+ipc://, nng+push+tcp://, etc.

Examples:
    file:///home/user/output.bin   - absolute file path
    socket:///tmp/my.sock          - Unix domain socket
    nng+push+ipc://tmp/keypoints   - NNG Push over IPC
    nng+push+tcp://host:5555       - NNG Push over TCP
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

    NNG_PUSH_IPC = auto()
    """NNG Push over IPC."""

    NNG_PUSH_TCP = auto()
    """NNG Push over TCP."""

    NNG_PULL_IPC = auto()
    """NNG Pull over IPC."""

    NNG_PULL_TCP = auto()
    """NNG Pull over TCP."""

    NNG_PUB_IPC = auto()
    """NNG Pub over IPC."""

    NNG_PUB_TCP = auto()
    """NNG Pub over TCP."""

    NNG_SUB_IPC = auto()
    """NNG Sub over IPC."""

    NNG_SUB_TCP = auto()
    """NNG Sub over TCP."""


class TransportProtocol:
    """
    Unified transport protocol specification as a value type.

    Supports: file://, socket://, nng+push+ipc://, nng+push+tcp://, etc.
    """

    # Predefined protocols
    File: TransportProtocol
    Socket: TransportProtocol
    NngPushIpc: TransportProtocol
    NngPushTcp: TransportProtocol
    NngPullIpc: TransportProtocol
    NngPullTcp: TransportProtocol
    NngPubIpc: TransportProtocol
    NngPubTcp: TransportProtocol
    NngSubIpc: TransportProtocol
    NngSubTcp: TransportProtocol

    _SCHEMA_MAP: ClassVar[Dict[str, TransportKind]] = {
        "file": TransportKind.FILE,
        "socket": TransportKind.SOCKET,
        "nng+push+ipc": TransportKind.NNG_PUSH_IPC,
        "nng+push+tcp": TransportKind.NNG_PUSH_TCP,
        "nng+pull+ipc": TransportKind.NNG_PULL_IPC,
        "nng+pull+tcp": TransportKind.NNG_PULL_TCP,
        "nng+pub+ipc": TransportKind.NNG_PUB_IPC,
        "nng+pub+tcp": TransportKind.NNG_PUB_TCP,
        "nng+sub+ipc": TransportKind.NNG_SUB_IPC,
        "nng+sub+tcp": TransportKind.NNG_SUB_TCP,
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
        """The schema string (e.g., 'file', 'socket', 'nng+push+ipc')."""
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

    @property
    def is_nng(self) -> bool:
        """True if this is any NNG-based transport."""
        return self._kind in {
            TransportKind.NNG_PUSH_IPC,
            TransportKind.NNG_PUSH_TCP,
            TransportKind.NNG_PULL_IPC,
            TransportKind.NNG_PULL_TCP,
            TransportKind.NNG_PUB_IPC,
            TransportKind.NNG_PUB_TCP,
            TransportKind.NNG_SUB_IPC,
            TransportKind.NNG_SUB_TCP,
        }

    @property
    def is_push(self) -> bool:
        """True if this is a Push pattern."""
        return self._kind in {TransportKind.NNG_PUSH_IPC, TransportKind.NNG_PUSH_TCP}

    @property
    def is_pull(self) -> bool:
        """True if this is a Pull pattern."""
        return self._kind in {TransportKind.NNG_PULL_IPC, TransportKind.NNG_PULL_TCP}

    @property
    def is_pub(self) -> bool:
        """True if this is a Pub pattern."""
        return self._kind in {TransportKind.NNG_PUB_IPC, TransportKind.NNG_PUB_TCP}

    @property
    def is_sub(self) -> bool:
        """True if this is a Sub pattern."""
        return self._kind in {TransportKind.NNG_SUB_IPC, TransportKind.NNG_SUB_TCP}

    @property
    def is_ipc(self) -> bool:
        """True if this uses IPC layer."""
        return self._kind in {
            TransportKind.NNG_PUSH_IPC,
            TransportKind.NNG_PULL_IPC,
            TransportKind.NNG_PUB_IPC,
            TransportKind.NNG_SUB_IPC,
        }

    @property
    def is_tcp(self) -> bool:
        """True if this uses TCP layer."""
        return self._kind in {
            TransportKind.NNG_PUSH_TCP,
            TransportKind.NNG_PULL_TCP,
            TransportKind.NNG_PUB_TCP,
            TransportKind.NNG_SUB_TCP,
        }

    def create_nng_address(self, path_or_host: str) -> str:
        """
        Create the NNG address from a path/host.

        For IPC: ipc:///path
        For TCP: tcp://host:port

        Raises:
            ValueError: If this is not an NNG protocol.
        """
        if not self.is_nng:
            raise ValueError(f"Cannot create NNG address for {self._kind} transport")

        if self.is_ipc:
            # IPC paths need leading "/" for absolute paths
            if not path_or_host.startswith("/"):
                return f"ipc:///{path_or_host}"
            return f"ipc://{path_or_host}"

        # TCP
        return f"tcp://{path_or_host}"

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
        """Parse a protocol string (e.g., 'nng+push+ipc')."""
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
TransportProtocol.NngPushIpc = TransportProtocol(TransportKind.NNG_PUSH_IPC, "nng+push+ipc")
TransportProtocol.NngPushTcp = TransportProtocol(TransportKind.NNG_PUSH_TCP, "nng+push+tcp")
TransportProtocol.NngPullIpc = TransportProtocol(TransportKind.NNG_PULL_IPC, "nng+pull+ipc")
TransportProtocol.NngPullTcp = TransportProtocol(TransportKind.NNG_PULL_TCP, "nng+pull+tcp")
TransportProtocol.NngPubIpc = TransportProtocol(TransportKind.NNG_PUB_IPC, "nng+pub+ipc")
TransportProtocol.NngPubTcp = TransportProtocol(TransportKind.NNG_PUB_TCP, "nng+pub+tcp")
TransportProtocol.NngSubIpc = TransportProtocol(TransportKind.NNG_SUB_IPC, "nng+sub+ipc")
TransportProtocol.NngSubTcp = TransportProtocol(TransportKind.NNG_SUB_TCP, "nng+sub+tcp")

# Initialize reverse lookup map
TransportProtocol._KIND_TO_SCHEMA = {v: k for k, v in TransportProtocol._SCHEMA_MAP.items()}
