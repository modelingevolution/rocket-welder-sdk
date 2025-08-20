"""
Exception classes for RocketWelder SDK
"""


class RocketWelderException(Exception):
    """Base exception for RocketWelder SDK"""
    pass


class ConnectionException(RocketWelderException):
    """Exception raised for connection errors"""
    pass


class ProtocolException(RocketWelderException):
    """Exception raised for protocol-specific errors"""
    pass


class BufferException(RocketWelderException):
    """Exception raised for buffer-related errors"""
    pass