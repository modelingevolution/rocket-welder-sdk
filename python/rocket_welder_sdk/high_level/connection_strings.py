"""
Strongly-typed connection strings with parsing support.

Connection string format: protocol://path?param1=value1&param2=value2

Examples:
    nng+push+ipc://tmp/keypoints?masterFrameInterval=300
    nng+pub+tcp://localhost:5555
    file:///path/to/output.bin
    socket:///tmp/my.sock
"""

from __future__ import annotations

import contextlib
import os
from dataclasses import dataclass, field
from enum import Enum, auto
from typing import Dict, Optional
from urllib.parse import parse_qs

from .transport_protocol import TransportProtocol


class VideoSourceType(Enum):
    """Type of video source."""

    CAMERA = auto()
    FILE = auto()
    SHARED_MEMORY = auto()
    RTSP = auto()
    HTTP = auto()


@dataclass(frozen=True)
class VideoSourceConnectionString:
    """
    Strongly-typed connection string for video source input.

    Supported formats:
    - "0", "1", etc. - Camera device index
    - file://path/to/video.mp4 - Video file
    - shm://buffer_name - Shared memory buffer
    - rtsp://host/stream - RTSP stream
    """

    value: str
    source_type: VideoSourceType
    camera_index: Optional[int] = None
    path: Optional[str] = None
    parameters: Dict[str, str] = field(default_factory=dict)

    @classmethod
    def default(cls) -> VideoSourceConnectionString:
        """Default video source (camera 0)."""
        return cls.parse("0")

    @classmethod
    def from_environment(cls, variable_name: str = "VIDEO_SOURCE") -> VideoSourceConnectionString:
        """Create from environment variable or use default."""
        value = os.environ.get(variable_name) or os.environ.get("CONNECTION_STRING")
        return cls.parse(value) if value else cls.default()

    @classmethod
    def parse(cls, s: str) -> VideoSourceConnectionString:
        """Parse a connection string."""
        result = cls.try_parse(s)
        if result is None:
            raise ValueError(f"Invalid video source connection string: {s}")
        return result

    @classmethod
    def try_parse(cls, s: str) -> Optional[VideoSourceConnectionString]:
        """Try to parse a connection string."""
        if not s or not s.strip():
            return None

        s = s.strip()
        parameters: Dict[str, str] = {}

        # Extract query parameters
        if "?" in s:
            base, query = s.split("?", 1)
            for key, values in parse_qs(query).items():
                parameters[key.lower()] = values[0] if values else ""
            s = base

        # Check for camera index first
        if s.isdigit():
            return cls(
                value=s,
                source_type=VideoSourceType.CAMERA,
                camera_index=int(s),
                parameters=parameters,
            )

        # Parse protocol
        if s.startswith("file://"):
            path = "/" + s[7:]  # Restore absolute path
            return cls(
                value=s,
                source_type=VideoSourceType.FILE,
                path=path,
                parameters=parameters,
            )
        elif s.startswith("shm://"):
            path = s[6:]
            return cls(
                value=s,
                source_type=VideoSourceType.SHARED_MEMORY,
                path=path,
                parameters=parameters,
            )
        elif s.startswith("rtsp://"):
            return cls(
                value=s,
                source_type=VideoSourceType.RTSP,
                path=s,
                parameters=parameters,
            )
        elif s.startswith("http://") or s.startswith("https://"):
            return cls(
                value=s,
                source_type=VideoSourceType.HTTP,
                path=s,
                parameters=parameters,
            )
        elif "://" not in s:
            # Assume file path
            return cls(
                value=s,
                source_type=VideoSourceType.FILE,
                path=s,
                parameters=parameters,
            )

        return None

    def __str__(self) -> str:
        return self.value


@dataclass(frozen=True)
class KeyPointsConnectionString:
    """
    Strongly-typed connection string for KeyPoints output.

    Supported protocols:
    - file:///path/to/file.bin - File output (absolute path)
    - socket:///tmp/socket.sock - Unix domain socket
    - nng+push+ipc://tmp/keypoints - NNG Push over IPC
    - nng+push+tcp://host:port - NNG Push over TCP

    Supported parameters:
    - masterFrameInterval: Interval between master frames (default: 300)
    """

    value: str
    protocol: TransportProtocol
    address: str
    master_frame_interval: int = 300
    parameters: Dict[str, str] = field(default_factory=dict)

    @classmethod
    def default(cls) -> KeyPointsConnectionString:
        """Default connection string for KeyPoints."""
        return cls.parse("nng+push+ipc://tmp/rocket-welder-keypoints?masterFrameInterval=300")

    @classmethod
    def from_environment(
        cls, variable_name: str = "KEYPOINTS_CONNECTION_STRING"
    ) -> KeyPointsConnectionString:
        """Create from environment variable or use default."""
        value = os.environ.get(variable_name)
        return cls.parse(value) if value else cls.default()

    @classmethod
    def parse(cls, s: str) -> KeyPointsConnectionString:
        """Parse a connection string."""
        result = cls.try_parse(s)
        if result is None:
            raise ValueError(f"Invalid KeyPoints connection string: {s}")
        return result

    @classmethod
    def try_parse(cls, s: str) -> Optional[KeyPointsConnectionString]:
        """Try to parse a connection string."""
        if not s or not s.strip():
            return None

        s = s.strip()
        parameters: Dict[str, str] = {}

        # Extract query parameters
        endpoint_part = s
        if "?" in s:
            endpoint_part, query = s.split("?", 1)
            for key, values in parse_qs(query).items():
                parameters[key.lower()] = values[0] if values else ""

        # Parse protocol and address
        scheme_end = endpoint_part.find("://")
        if scheme_end <= 0:
            return None

        schema_str = endpoint_part[:scheme_end]
        path_part = endpoint_part[scheme_end + 3 :]  # skip "://"

        protocol = TransportProtocol.try_parse(schema_str)
        if protocol is None:
            return None

        # Build address based on protocol type
        if protocol.is_file:
            # file:///absolute/path -> /absolute/path
            address = path_part if path_part.startswith("/") else "/" + path_part
        elif protocol.is_socket:
            # socket:///tmp/sock -> /tmp/sock
            address = path_part if path_part.startswith("/") else "/" + path_part
        elif protocol.is_nng:
            # NNG protocols need proper address format
            address = protocol.create_nng_address(path_part)
        else:
            return None

        # Parse masterFrameInterval
        master_frame_interval = 300  # default
        if "masterframeinterval" in parameters:
            with contextlib.suppress(ValueError):
                master_frame_interval = int(parameters["masterframeinterval"])

        return cls(
            value=s,
            protocol=protocol,
            address=address,
            master_frame_interval=master_frame_interval,
            parameters=parameters,
        )

    def __str__(self) -> str:
        return self.value


@dataclass(frozen=True)
class SegmentationConnectionString:
    """
    Strongly-typed connection string for Segmentation output.

    Supported protocols:
    - file:///path/to/file.bin - File output (absolute path)
    - socket:///tmp/socket.sock - Unix domain socket
    - nng+push+ipc://tmp/segmentation - NNG Push over IPC
    - nng+push+tcp://host:port - NNG Push over TCP
    """

    value: str
    protocol: TransportProtocol
    address: str
    parameters: Dict[str, str] = field(default_factory=dict)

    @classmethod
    def default(cls) -> SegmentationConnectionString:
        """Default connection string for Segmentation."""
        return cls.parse("nng+push+ipc://tmp/rocket-welder-segmentation")

    @classmethod
    def from_environment(
        cls, variable_name: str = "SEGMENTATION_CONNECTION_STRING"
    ) -> SegmentationConnectionString:
        """Create from environment variable or use default."""
        value = os.environ.get(variable_name)
        return cls.parse(value) if value else cls.default()

    @classmethod
    def parse(cls, s: str) -> SegmentationConnectionString:
        """Parse a connection string."""
        result = cls.try_parse(s)
        if result is None:
            raise ValueError(f"Invalid Segmentation connection string: {s}")
        return result

    @classmethod
    def try_parse(cls, s: str) -> Optional[SegmentationConnectionString]:
        """Try to parse a connection string."""
        if not s or not s.strip():
            return None

        s = s.strip()
        parameters: Dict[str, str] = {}

        # Extract query parameters
        endpoint_part = s
        if "?" in s:
            endpoint_part, query = s.split("?", 1)
            for key, values in parse_qs(query).items():
                parameters[key.lower()] = values[0] if values else ""

        # Parse protocol and address
        scheme_end = endpoint_part.find("://")
        if scheme_end <= 0:
            return None

        schema_str = endpoint_part[:scheme_end]
        path_part = endpoint_part[scheme_end + 3 :]  # skip "://"

        protocol = TransportProtocol.try_parse(schema_str)
        if protocol is None:
            return None

        # Build address based on protocol type
        if protocol.is_file:
            # file:///absolute/path -> /absolute/path
            address = path_part if path_part.startswith("/") else "/" + path_part
        elif protocol.is_socket:
            # socket:///tmp/sock -> /tmp/sock
            address = path_part if path_part.startswith("/") else "/" + path_part
        elif protocol.is_nng:
            # NNG protocols need proper address format
            address = protocol.create_nng_address(path_part)
        else:
            return None

        return cls(
            value=s,
            protocol=protocol,
            address=address,
            parameters=parameters,
        )

    def __str__(self) -> str:
        return self.value
