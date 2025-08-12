"""
RocketWelder client implementation.
"""

import os
import sys
import time
import threading
from typing import Optional, Callable, Dict, Any, Iterator
import numpy as np
import cv2


class Client:
    """Client for RocketWelder video streaming services."""
    
    def __init__(self, connection_string: str):
        """
        Initialize client with connection string.
        
        Args:
            connection_string: Connection string (e.g., "shm://buffer_name")
        """
        self.connection_string = connection_string
        self._callback: Optional[Callable[[np.ndarray], None]] = None
        self._callback_with_metadata: Optional[Callable[[np.ndarray, Dict[str, Any]], None]] = None
        self._running = False
        self._thread: Optional[threading.Thread] = None
        
        # TODO: Parse connection string
        # TODO: Initialize based on protocol (shm://, mjpeg+http://, etc.)
    
    @classmethod
    def from_args(cls, argv: list) -> 'Client':
        """
        Create client from command line arguments and environment variables.
        Environment variable CONNECTION_STRING is checked first, then overridden by args.
        
        Args:
            argv: Command line arguments (typically sys.argv)
        
        Returns:
            Client instance
        """
        # Check environment variable first
        connection_string = os.environ.get('CONNECTION_STRING')
        
        # Override with command line args if present
        if argv:
            for arg in argv[1:]:  # Skip program name
                if (arg.startswith('shm://') or 
                    arg.startswith('mjpeg+http://') or 
                    arg.startswith('mjpeg+tcp://')):
                    connection_string = arg
                    break
        
        return cls(connection_string or 'shm://default')
    
    @classmethod
    def from_env(cls) -> 'Client':
        """
        Create client from environment variable CONNECTION_STRING.
        
        Returns:
            Client instance
        """
        connection_string = os.environ.get('CONNECTION_STRING', 'shm://default')
        return cls(connection_string)
    
    @classmethod
    def from_connection_string(cls, connection_string: str) -> 'Client':
        """
        Create client from a specific connection string.
        
        Args:
            connection_string: Connection string
        
        Returns:
            Client instance
        """
        return cls(connection_string)
    
    def on_frame(self, callback: Callable) -> Callable:
        """
        Decorator/method to set frame processing callback.
        
        The callback can have one of these signatures:
        - callback(frame: np.ndarray) -> None
        - callback(frame: np.ndarray, metadata: dict) -> None
        
        Args:
            callback: Function to process frames
        
        Returns:
            The callback function (for decorator usage)
        """
        import inspect
        sig = inspect.signature(callback)
        param_count = len(sig.parameters)
        
        if param_count == 1:
            self._callback = callback
        elif param_count == 2:
            self._callback_with_metadata = callback
        else:
            raise ValueError(f"Callback must have 1 or 2 parameters, got {param_count}")
        
        return callback
    
    def start(self):
        """Start frame processing."""
        if self._running:
            return
        
        if not self._callback and not self._callback_with_metadata:
            raise RuntimeError("Frame callback must be set before starting")
        
        self._running = True
        self._thread = threading.Thread(target=self._process_frames)
        self._thread.daemon = True
        self._thread.start()
    
    def stop(self):
        """Stop frame processing."""
        if not self._running:
            return
        
        self._running = False
        if self._thread:
            self._thread.join(timeout=5.0)
            self._thread = None
    
    def is_running(self) -> bool:
        """Check if client is running."""
        return self._running
    
    def frames(self) -> Iterator[np.ndarray]:
        """
        Iterator interface for frame processing.
        
        Yields:
            Frame as numpy array
        """
        # TODO: Implement actual frame reading
        # For now, generate dummy frames for testing
        while True:
            # Create dummy frame
            frame = np.zeros((480, 640, 3), dtype=np.uint8)
            yield frame
            time.sleep(0.033)  # ~30 FPS
    
    def _process_frames(self):
        """Internal method to process frames in thread."""
        # TODO: Implement actual frame processing
        # For now, generate dummy frames for testing
        while self._running:
            # Create dummy frame
            frame = np.zeros((480, 640, 3), dtype=np.uint8)
            
            # Create dummy metadata
            metadata = {
                'timestamp': time.time(),
                'format': 'BGR',
                'width': 640,
                'height': 480
            }
            
            # Call appropriate callback
            if self._callback:
                self._callback(frame)
            elif self._callback_with_metadata:
                self._callback_with_metadata(frame, metadata)
            
            time.sleep(0.033)  # ~30 FPS
    
    def __enter__(self):
        """Context manager entry."""
        self.start()
        return self
    
    def __exit__(self, exc_type, exc_val, exc_tb):
        """Context manager exit."""
        self.stop()