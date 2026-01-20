"""
Transport layer for RocketWelder SDK.

Provides transport-agnostic frame sink/source abstractions for protocols.
"""

from .frame_sink import IFrameSink, NullFrameSink
from .frame_source import IFrameSource
from .stream_transport import StreamFrameSink, StreamFrameSource
from .tcp_transport import TcpFrameSink, TcpFrameSource
from .unix_socket_transport import (
    UnixSocketFrameSink,
    UnixSocketFrameSource,
    UnixSocketServer,
)
from .websocket_transport import (
    WebSocketFrameSink,
    WebSocketFrameSource,
    connect_websocket_sink,
    connect_websocket_source,
)

__all__ = [
    "IFrameSink",
    "IFrameSource",
    "NullFrameSink",
    "StreamFrameSink",
    "StreamFrameSource",
    "TcpFrameSink",
    "TcpFrameSource",
    "UnixSocketFrameSink",
    "UnixSocketFrameSource",
    "UnixSocketServer",
    "WebSocketFrameSink",
    "WebSocketFrameSource",
    "connect_websocket_sink",
    "connect_websocket_source",
]
