"""
Transport layer for RocketWelder SDK.

Provides transport-agnostic frame sink/source abstractions for protocols.
"""

from .frame_sink import IFrameSink, NullFrameSink
from .frame_source import IFrameSource
from .nng_transport import NngFrameSink, NngFrameSource
from .stream_transport import StreamFrameSink, StreamFrameSource
from .tcp_transport import TcpFrameSink, TcpFrameSource
from .unix_socket_transport import (
    UnixSocketFrameSink,
    UnixSocketFrameSource,
    UnixSocketServer,
)

__all__ = [
    "IFrameSink",
    "IFrameSource",
    "NngFrameSink",
    "NngFrameSource",
    "NullFrameSink",
    "StreamFrameSink",
    "StreamFrameSource",
    "TcpFrameSink",
    "TcpFrameSource",
    "UnixSocketFrameSink",
    "UnixSocketFrameSource",
    "UnixSocketServer",
]
