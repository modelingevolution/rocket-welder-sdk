"""Frame sink abstraction for writing frames to any transport."""

from abc import ABC, abstractmethod


class IFrameSink(ABC):
    """
    Low-level abstraction for writing discrete frames to any transport.

    Transport-agnostic interface that handles the question: "where do frames go?"
    This abstraction decouples protocol logic (KeyPoints, SegmentationResults) from
    transport mechanisms (File, TCP, WebSocket, NNG). Each frame is written atomically.
    """

    @abstractmethod
    def write_frame(self, frame_data: bytes) -> None:
        """
        Write a complete frame to the underlying transport synchronously.

        Args:
            frame_data: Complete frame data to write
        """
        pass

    @abstractmethod
    async def write_frame_async(self, frame_data: bytes) -> None:
        """
        Write a complete frame to the underlying transport asynchronously.

        Args:
            frame_data: Complete frame data to write
        """
        pass

    @abstractmethod
    def flush(self) -> None:
        """
        Flush any buffered data to the transport synchronously.

        For message-based transports (NNG, WebSocket), this may be a no-op.
        """
        pass

    @abstractmethod
    async def flush_async(self) -> None:
        """
        Flush any buffered data to the transport asynchronously.

        For message-based transports (NNG, WebSocket), this may be a no-op.
        """
        pass

    def __enter__(self) -> "IFrameSink":
        """Context manager entry."""
        return self

    def __exit__(self, *args: object) -> None:
        """Context manager exit."""
        self.close()

    async def __aenter__(self) -> "IFrameSink":
        """Async context manager entry."""
        return self

    async def __aexit__(self, *args: object) -> None:
        """Async context manager exit."""
        await self.close_async()

    @abstractmethod
    def close(self) -> None:
        """Close the sink and release resources."""
        pass

    @abstractmethod
    async def close_async(self) -> None:
        """Close the sink and release resources asynchronously."""
        pass


class NullFrameSink(IFrameSink):
    """
    A frame sink that discards all data.

    Use when no output URL is configured or for testing.
    Singleton pattern - use NullFrameSink.instance() to get the shared instance.
    """

    _instance: "NullFrameSink | None" = None

    def __new__(cls) -> "NullFrameSink":
        if cls._instance is None:
            cls._instance = super().__new__(cls)
        return cls._instance

    @classmethod
    def instance(cls) -> "NullFrameSink":
        """Get the singleton instance."""
        return cls()

    def write_frame(self, frame_data: bytes) -> None:
        """Discards the frame data (no-op)."""
        pass

    async def write_frame_async(self, frame_data: bytes) -> None:
        """Discards the frame data (no-op)."""
        pass

    def flush(self) -> None:
        """No-op flush."""
        pass

    async def flush_async(self) -> None:
        """No-op flush."""
        pass

    def close(self) -> None:
        """No-op close (singleton, never actually closed)."""
        pass

    async def close_async(self) -> None:
        """No-op close (singleton, never actually closed)."""
        pass
