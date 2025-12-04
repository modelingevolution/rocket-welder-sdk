"""
Transport protocol types with composable + operator.

Allows building transport protocols like:
    protocol = Transport.Nng + Transport.Push + Transport.Ipc
    # Results in TransportProtocol("nng", "push", "ipc")
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Optional


@dataclass(frozen=True)
class MessagingLibrary:
    """Messaging library (nng, zeromq, etc.)."""

    name: str

    def __add__(self, pattern: MessagingPattern) -> TransportBuilder:
        """Compose with messaging pattern: Nng + Push."""
        return TransportBuilder(library=self, pattern=pattern)

    def __str__(self) -> str:
        return self.name


@dataclass(frozen=True)
class MessagingPattern:
    """Messaging pattern (push/pull, pub/sub, etc.)."""

    name: str

    def __str__(self) -> str:
        return self.name


@dataclass(frozen=True)
class TransportLayer:
    """Transport layer (ipc, tcp, etc.)."""

    name: str
    uri_prefix: str

    def __str__(self) -> str:
        return self.name


@dataclass(frozen=True)
class TransportBuilder:
    """Builder for constructing transport protocols."""

    library: MessagingLibrary
    pattern: MessagingPattern

    def __add__(self, layer: TransportLayer) -> TransportProtocol:
        """Compose with transport layer: (Nng + Push) + Ipc."""
        return TransportProtocol(library=self.library, pattern=self.pattern, layer=layer)

    def __str__(self) -> str:
        return f"{self.library}+{self.pattern}"


@dataclass(frozen=True)
class TransportProtocol:
    """Complete transport protocol specification."""

    library: MessagingLibrary
    pattern: MessagingPattern
    layer: TransportLayer

    @property
    def protocol_string(self) -> str:
        """Protocol string for parsing (e.g., 'nng+push+ipc')."""
        return f"{self.library}+{self.pattern}+{self.layer}"

    def create_nng_address(self, path_or_host: str) -> str:
        """
        Create the NNG address from a path/host.

        For IPC: adds leading "/" to make absolute path
        For TCP: uses as-is
        """
        if self.layer == Transport.Ipc and not path_or_host.startswith("/"):
            return f"{self.layer.uri_prefix}/{path_or_host}"
        return f"{self.layer.uri_prefix}{path_or_host}"

    @property
    def is_push(self) -> bool:
        """Check if this is a push pattern."""
        return self.pattern == Transport.Push

    @property
    def is_pub(self) -> bool:
        """Check if this is a pub pattern."""
        return self.pattern == Transport.Pub

    def __str__(self) -> str:
        return self.protocol_string

    @classmethod
    def parse(cls, s: str) -> TransportProtocol:
        """Parse a protocol string (e.g., 'nng+push+ipc')."""
        result = cls.try_parse(s)
        if result is None:
            raise ValueError(f"Invalid transport protocol: {s}")
        return result

    @classmethod
    def try_parse(cls, s: str) -> Optional[TransportProtocol]:
        """Try to parse a protocol string."""
        if not s:
            return None

        parts = s.lower().split("+")
        if len(parts) != 3:
            return None

        # Parse library
        if parts[0] == "nng":
            library = Transport.Nng
        else:
            return None

        # Parse pattern
        if parts[1] == "push":
            pattern = Transport.Push
        elif parts[1] == "pull":
            pattern = Transport.Pull
        elif parts[1] == "pub":
            pattern = Transport.Pub
        elif parts[1] == "sub":
            pattern = Transport.Sub
        else:
            return None

        # Parse layer
        if parts[2] == "ipc":
            layer = Transport.Ipc
        elif parts[2] == "tcp":
            layer = Transport.Tcp
        else:
            return None

        return cls(library=library, pattern=pattern, layer=layer)


class Transport:
    """Static helpers for building transport protocols using + operator."""

    # Messaging libraries
    Nng: MessagingLibrary = MessagingLibrary("nng")

    # Messaging patterns
    Push: MessagingPattern = MessagingPattern("push")
    Pull: MessagingPattern = MessagingPattern("pull")
    Pub: MessagingPattern = MessagingPattern("pub")
    Sub: MessagingPattern = MessagingPattern("sub")

    # Transport layers
    Ipc: TransportLayer = TransportLayer("ipc", "ipc://")
    Tcp: TransportLayer = TransportLayer("tcp", "tcp://")

    # File output (not a real transport)
    File: str = "file"
