"""Frame source abstraction for reading frames from any transport."""

from abc import ABC, abstractmethod
from typing import Optional


class IFrameSource(ABC):
    """
    Low-level abstraction for reading discrete frames from any transport.

    Transport-agnostic interface that handles the question: "where do frames come from?"
    This abstraction decouples protocol logic (KeyPoints, SegmentationResults) from
    transport mechanisms (File, TCP, WebSocket, NNG). Each frame is read atomically.
    """

    @abstractmethod
    def read_frame(self) -> Optional[bytes]:
        """
        Read a complete frame from the underlying transport synchronously.

        Returns:
            Complete frame data, or None if end of stream/no more messages
        """
        pass

    @abstractmethod
    async def read_frame_async(self) -> Optional[bytes]:
        """
        Read a complete frame from the underlying transport asynchronously.

        Returns:
            Complete frame data, or None if end of stream/no more messages
        """
        pass

    @property
    @abstractmethod
    def has_more_frames(self) -> bool:
        """
        Check if more frames are available.

        For streaming transports (file), this checks for EOF.
        For message-based transports (NNG), this may always return True until disconnection.

        Returns:
            True if more frames are available, False otherwise
        """
        pass

    def __enter__(self) -> "IFrameSource":
        """Context manager entry."""
        return self

    def __exit__(self, *args: object) -> None:
        """Context manager exit."""
        self.close()

    async def __aenter__(self) -> "IFrameSource":
        """Async context manager entry."""
        return self

    async def __aexit__(self, *args: object) -> None:
        """Async context manager exit."""
        await self.close_async()

    @abstractmethod
    def close(self) -> None:
        """Close the source and release resources."""
        pass

    @abstractmethod
    async def close_async(self) -> None:
        """Close the source and release resources asynchronously."""
        pass
