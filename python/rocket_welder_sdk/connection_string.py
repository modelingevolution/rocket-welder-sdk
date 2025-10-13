"""
Enterprise-grade Connection String implementation for RocketWelder SDK.
Matches C# ConnectionString struct functionality.
"""

from __future__ import annotations

import contextlib
from dataclasses import dataclass, field
from enum import Enum, Flag, auto
from typing import Any

from .bytes_size import BytesSize


class Protocol(Flag):
    """Protocol flags for connection types."""

    NONE = 0
    SHM = auto()  # Shared memory
    MJPEG = auto()  # Motion JPEG
    HTTP = auto()  # HTTP protocol
    TCP = auto()  # TCP protocol
    FILE = auto()  # File protocol


class ConnectionMode(Enum):
    """Connection mode for duplex/one-way communication."""

    ONE_WAY = "OneWay"
    DUPLEX = "Duplex"

    def __str__(self) -> str:
        """String representation."""
        return self.value


@dataclass(frozen=True)
class ConnectionString:
    """
    Immutable connection string representation for RocketWelder SDK.

    Supports parsing connection strings like:
    - shm://buffer_name?size=256MB&metadata=4KB&mode=Duplex
    - mjpeg://192.168.1.100:8080
    - mjpeg+http://camera.local:80
    - file:///path/to/video.mp4?loop=true
    """

    protocol: Protocol
    host: str | None = None
    port: int | None = None
    buffer_name: str | None = None
    file_path: str | None = None
    parameters: dict[str, str] = field(default_factory=dict)
    buffer_size: BytesSize = field(default_factory=lambda: BytesSize.parse("256MB"))
    metadata_size: BytesSize = field(default_factory=lambda: BytesSize.parse("4KB"))
    connection_mode: ConnectionMode = ConnectionMode.ONE_WAY
    timeout_ms: int = 5000

    @classmethod
    def parse(cls, connection_string: str) -> ConnectionString:
        """
        Parse a connection string into a ConnectionString object.

        Args:
            connection_string: Connection string to parse

        Returns:
            ConnectionString instance

        Raises:
            ValueError: If the connection string format is invalid
        """
        if not connection_string:
            raise ValueError("Connection string cannot be empty")

        # Handle special protocols
        if "://" not in connection_string:
            raise ValueError(f"Invalid connection string format: {connection_string}")

        # Split protocol and remainder
        protocol_str, remainder = connection_string.split("://", 1)
        protocol = cls._parse_protocol(protocol_str)

        # Parse based on protocol type
        if protocol == Protocol.SHM:
            return cls._parse_shm(protocol, remainder)
        elif protocol == Protocol.FILE:
            return cls._parse_file(protocol, remainder)
        elif bool(protocol & Protocol.MJPEG):  # type: ignore[operator]
            return cls._parse_mjpeg(protocol, remainder)
        else:
            raise ValueError(f"Unsupported protocol: {protocol_str}")

    @classmethod
    def _parse_protocol(cls, protocol_str: str) -> Protocol:
        """Parse protocol string into Protocol flags."""
        protocol_str = protocol_str.lower()

        # Handle combined protocols (e.g., mjpeg+http)
        if "+" in protocol_str:
            parts = protocol_str.split("+")
            result = Protocol.NONE
            for part in parts:
                result |= cls._get_single_protocol(part)
            return result
        else:
            return cls._get_single_protocol(protocol_str)

    @classmethod
    def _get_single_protocol(cls, protocol_str: str) -> Protocol:
        """Get a single protocol from string."""
        protocol_map = {
            "shm": Protocol.SHM,
            "mjpeg": Protocol.MJPEG,
            "http": Protocol.HTTP,
            "tcp": Protocol.TCP,
            "file": Protocol.FILE,
        }

        protocol = protocol_map.get(protocol_str, Protocol.NONE)
        if protocol == Protocol.NONE:
            raise ValueError(f"Unknown protocol: {protocol_str}")
        return protocol

    @classmethod
    def _parse_shm(cls, protocol: Protocol, remainder: str) -> ConnectionString:
        """Parse shared memory connection string."""
        # Split buffer name and query parameters
        if "?" in remainder:
            buffer_name, query_string = remainder.split("?", 1)
            params = cls._parse_query_params(query_string)
        else:
            buffer_name = remainder
            params = {}

        # Parse parameters
        buffer_size = BytesSize.parse("256MB")
        metadata_size = BytesSize.parse("4KB")
        connection_mode = ConnectionMode.ONE_WAY
        timeout_ms = 5000

        if "size" in params:
            buffer_size = BytesSize.parse(params["size"])
        if "metadata" in params:
            metadata_size = BytesSize.parse(params["metadata"])
        if "mode" in params:
            mode_str = params["mode"].upper()
            if mode_str == "DUPLEX":
                connection_mode = ConnectionMode.DUPLEX
            elif mode_str == "ONEWAY" or mode_str == "ONE_WAY":
                connection_mode = ConnectionMode.ONE_WAY
        if "timeout" in params:
            with contextlib.suppress(ValueError):
                timeout_ms = int(params["timeout"])

        return cls(
            protocol=protocol,
            buffer_name=buffer_name,
            buffer_size=buffer_size,
            metadata_size=metadata_size,
            connection_mode=connection_mode,
            timeout_ms=timeout_ms,
        )

    @classmethod
    def _parse_file(cls, protocol: Protocol, remainder: str) -> ConnectionString:
        """Parse file protocol connection string."""
        # Split file path and query parameters
        if "?" in remainder:
            file_path, query_string = remainder.split("?", 1)
            params = cls._parse_query_params(query_string)
        else:
            file_path = remainder
            params = {}

        # Handle file:///absolute/path and file://relative/path
        if not file_path.startswith("/"):
            file_path = "/" + file_path

        # Parse common parameters
        connection_mode = ConnectionMode.ONE_WAY
        timeout_ms = 5000

        if "mode" in params:
            mode_str = params["mode"].upper()
            if mode_str == "DUPLEX":
                connection_mode = ConnectionMode.DUPLEX
            elif mode_str in ("ONEWAY", "ONE_WAY"):
                connection_mode = ConnectionMode.ONE_WAY
        if "timeout" in params:
            with contextlib.suppress(ValueError):
                timeout_ms = int(params["timeout"])

        return cls(
            protocol=protocol,
            file_path=file_path,
            parameters=params,
            connection_mode=connection_mode,
            timeout_ms=timeout_ms,
        )

    @classmethod
    def _parse_mjpeg(cls, protocol: Protocol, remainder: str) -> ConnectionString:
        """Parse MJPEG connection string."""
        # Split host:port and query parameters
        if "?" in remainder:
            host_port, query_string = remainder.split("?", 1)
            params = cls._parse_query_params(query_string)
        else:
            host_port = remainder
            params = {}

        # Parse host:port format
        if ":" in host_port:
            host, port_str = host_port.rsplit(":", 1)
            try:
                port = int(port_str)
            except ValueError as e:
                raise ValueError(f"Invalid port number: {port_str}") from e
        else:
            host = host_port
            # Default ports based on protocol
            port = 80 if Protocol.HTTP in protocol else 8080

        return cls(protocol=protocol, host=host, port=port, parameters=params)

    @classmethod
    def _parse_query_params(cls, query_string: str) -> dict[str, str]:
        """Parse query parameters from string."""
        params: dict[str, str] = {}
        if not query_string:
            return params

        pairs = query_string.split("&")
        for pair in pairs:
            if "=" in pair:
                key, value = pair.split("=", 1)
                params[key.lower()] = value

        return params

    def __str__(self) -> str:
        """Convert to connection string format."""
        # Format protocol
        protocol_parts = []
        if self.protocol & Protocol.SHM:
            protocol_parts.append("shm")
        if self.protocol & Protocol.FILE:
            protocol_parts.append("file")
        if self.protocol & Protocol.MJPEG:
            protocol_parts.append("mjpeg")
        if self.protocol & Protocol.HTTP:
            protocol_parts.append("http")
        if self.protocol & Protocol.TCP:
            protocol_parts.append("tcp")

        protocol_str = "+".join(protocol_parts)

        # Format based on protocol type
        if self.protocol == Protocol.SHM:
            params = [
                f"size={self.buffer_size}",
                f"metadata={self.metadata_size}",
                f"mode={self.connection_mode.value}",
            ]
            if self.timeout_ms != 5000:
                params.append(f"timeout={self.timeout_ms}")

            return f"{protocol_str}://{self.buffer_name}?{'&'.join(params)}"
        elif self.protocol == Protocol.FILE:
            query_string = ""
            if self.parameters:
                query_string = "?" + "&".join(f"{k}={v}" for k, v in self.parameters.items())
            return f"{protocol_str}://{self.file_path}{query_string}"
        else:
            return f"{protocol_str}://{self.host}:{self.port}"

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary representation."""
        return {
            "protocol": str(self.protocol),
            "host": self.host,
            "port": self.port,
            "buffer_name": self.buffer_name,
            "buffer_size": str(self.buffer_size),
            "metadata_size": str(self.metadata_size),
            "connection_mode": self.connection_mode.value,
            "timeout_ms": self.timeout_ms,
        }
