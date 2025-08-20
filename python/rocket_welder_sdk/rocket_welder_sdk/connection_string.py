"""
Connection string parsing for RocketWelder SDK

Mirrors the C# implementation with Python idioms.
"""

from dataclasses import dataclass
from enum import Flag, auto
from typing import Optional, Dict, Any
from urllib.parse import urlparse, parse_qs
import os


class Protocol(Flag):
    """Protocol flags that can be combined using bitwise operations"""
    NONE = 0
    SHM = auto()
    MJPEG = auto()
    HTTP = auto()
    TCP = auto()
    
    def __add__(self, other):
        """Support + operator as bitwise OR"""
        return self | other


@dataclass(frozen=True)
class ConnectionString:
    """
    Readonly connection string configuration
    
    Mirrors C# readonly record struct with IParsable interface.
    """
    protocol: Protocol
    host: Optional[str] = None
    port: Optional[int] = None
    path: Optional[str] = None
    buffer_name: Optional[str] = None
    buffer_size: int = 10485760  # 10MB default
    metadata_size: int = 65536  # 64KB default
    mode: str = "oneway"  # "oneway" or "duplex"
    
    @classmethod
    def parse(cls, s: str, provider=None) -> 'ConnectionString':
        """
        Parse connection string (equivalent to IParsable<T>.Parse in C#)
        
        Args:
            s: Connection string in format protocol://host:port/path?params
            provider: Not used, kept for API compatibility with C#
        
        Returns:
            ConnectionString instance
        
        Raises:
            ValueError: If connection string format is invalid
        """
        if not s:
            raise ValueError("Connection string cannot be empty")
        
        # Handle environment variable
        if s.startswith("$"):
            env_var = s[1:]
            s = os.environ.get(env_var, "")
            if not s:
                raise ValueError(f"Environment variable {env_var} not set")
        
        # Parse URL
        parsed = urlparse(s)
        
        # Determine protocol
        protocol = Protocol.NONE
        if parsed.scheme == "shm":
            protocol = Protocol.SHM
        elif parsed.scheme == "mjpeg+http":
            protocol = Protocol.MJPEG | Protocol.HTTP
        elif parsed.scheme == "mjpeg+tcp":
            protocol = Protocol.MJPEG | Protocol.TCP
        elif parsed.scheme == "http":
            protocol = Protocol.HTTP
        elif parsed.scheme == "tcp":
            protocol = Protocol.TCP
        else:
            raise ValueError(f"Unknown protocol: {parsed.scheme}")
        
        # Parse query parameters
        params = parse_qs(parsed.query) if parsed.query else {}
        
        # Extract components based on protocol
        host = None
        port = None
        path = None
        buffer_name = None
        
        if protocol == Protocol.SHM:
            # For SHM, the netloc or path becomes the buffer name
            buffer_name = parsed.netloc or parsed.path.lstrip("/") or "default"
        else:
            # For network protocols
            host = parsed.hostname
            port = parsed.port
            path = parsed.path.lstrip("/") if parsed.path else None
        
        # Extract additional parameters
        buffer_size = int(params.get("buffer_size", [10485760])[0])
        metadata_size = int(params.get("metadata_size", [65536])[0])
        mode = params.get("mode", ["oneway"])[0]
        
        return cls(
            protocol=protocol,
            host=host,
            port=port,
            path=path,
            buffer_name=buffer_name,
            buffer_size=buffer_size,
            metadata_size=metadata_size,
            mode=mode
        )
    
    @classmethod
    def try_parse(cls, s: str, provider=None) -> tuple[bool, Optional['ConnectionString']]:
        """
        Try to parse connection string (equivalent to IParsable<T>.TryParse in C#)
        
        Args:
            s: Connection string to parse
            provider: Not used, kept for API compatibility
        
        Returns:
            Tuple of (success, ConnectionString or None)
        """
        try:
            result = cls.parse(s, provider)
            return True, result
        except (ValueError, KeyError):
            return False, None
    
    @classmethod
    def from_config(cls, config: Dict[str, Any]) -> 'ConnectionString':
        """
        Create from configuration dictionary (mirrors IConfiguration in C#)
        
        Args:
            config: Configuration dictionary
        
        Returns:
            ConnectionString instance
        """
        # Try to get connection string from various sources
        connection_string = (
            config.get("CONNECTION_STRING") or
            config.get("RocketWelder", {}).get("ConnectionString") or
            config.get("ConnectionString") or
            os.environ.get("CONNECTION_STRING")
        )
        
        if connection_string:
            return cls.parse(connection_string)
        
        # Build from components
        protocol_str = config.get("RocketWelder", {}).get("Protocol", "shm")
        host = config.get("RocketWelder", {}).get("Host")
        port = config.get("RocketWelder", {}).get("Port")
        path = config.get("RocketWelder", {}).get("Path") or config.get("RocketWelder", {}).get("BufferName")
        
        if protocol_str == "shm":
            connection_string = f"shm://{path or 'default'}"
        elif host:
            port_part = f":{port}" if port else ""
            path_part = f"/{path}" if path else ""
            connection_string = f"{protocol_str}://{host}{port_part}{path_part}"
        else:
            connection_string = "shm://default"
        
        return cls.parse(connection_string)
    
    def __str__(self) -> str:
        """String representation of connection string"""
        if self.protocol == Protocol.SHM:
            return f"shm://{self.buffer_name or 'default'}?buffer_size={self.buffer_size}&metadata_size={self.metadata_size}&mode={self.mode}"
        elif self.protocol == (Protocol.MJPEG | Protocol.HTTP):
            port_part = f":{self.port}" if self.port else ""
            path_part = f"/{self.path}" if self.path else ""
            return f"mjpeg+http://{self.host}{port_part}{path_part}"
        elif self.protocol == (Protocol.MJPEG | Protocol.TCP):
            port_part = f":{self.port}" if self.port else ""
            path_part = f"/{self.path}" if self.path else ""
            return f"mjpeg+tcp://{self.host}{port_part}{path_part}"
        else:
            return f"{self.protocol.name.lower()}://{self.host}:{self.port}/{self.path}"