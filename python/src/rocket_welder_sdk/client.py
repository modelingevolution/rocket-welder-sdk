"""
RocketWelder client implementation with zero-copy frame processing

Mirrors the C# implementation with Python idioms.
"""

import os
import sys
import json
import logging
import threading
import struct
from typing import Optional, Callable, Dict, Any
from pathlib import Path

import cv2
import numpy as np

# Add ZeroBuffer to path
zerobuffer_path = Path("/mnt/d/source/modelingevolution/streamer/src/zerobuffer/python")
if zerobuffer_path.exists() and str(zerobuffer_path) not in sys.path:
    sys.path.insert(0, str(zerobuffer_path))

from zerobuffer import Reader, Writer, BufferConfig
from zerobuffer.exceptions import WriterDeadException

from .connection_string import ConnectionString, Protocol
from .gst_caps import GstCaps
from .exceptions import RocketWelderException, ConnectionException


class RocketWelderClient:
    """
    Client for RocketWelder video streaming services
    
    Provides zero-copy access to video frames from shared memory or network streams.
    """
    
    def __init__(self, connection_string: str, logger: Optional[logging.Logger] = None):
        """
        Initialize client with connection string
        
        Args:
            connection_string: Connection string in format protocol://host:port/path
            logger: Optional logger instance
        """
        if not connection_string:
            raise ValueError("Connection string cannot be empty")
        
        self._connection = ConnectionString.parse(connection_string)
        self._logger = logger or logging.getLogger(__name__)
        self._frame_callback: Optional[Callable[[np.ndarray], None]] = None
        self._running = False
        self._thread: Optional[threading.Thread] = None
        self._stop_event = threading.Event()
        
        # ZeroBuffer components
        self._reader: Optional[Reader] = None
        self._writer: Optional[Writer] = None
        
        # Cached video format from metadata
        self._video_format: Optional[GstCaps] = None
        
    @classmethod
    def from_args(cls, args: list) -> 'RocketWelderClient':
        """
        Create client from command line arguments
        
        Args:
            args: Command line arguments
        
        Returns:
            RocketWelderClient instance
        """
        # Check environment variable first
        connection_string = os.environ.get("CONNECTION_STRING")
        
        # Override with command line args if present
        if args:
            for arg in args:
                if (arg.startswith("shm://") or 
                    arg.startswith("mjpeg+http://") or 
                    arg.startswith("mjpeg+tcp://")):
                    connection_string = arg
                    break
        
        return cls(connection_string or "shm://default")
    
    @classmethod
    def from_config(cls, config: Dict[str, Any], logger: Optional[logging.Logger] = None) -> 'RocketWelderClient':
        """
        Create client from configuration dictionary
        
        Args:
            config: Configuration dictionary
            logger: Optional logger instance
        
        Returns:
            RocketWelderClient instance
        """
        connection_string = ConnectionString.from_config(config)
        return cls(str(connection_string), logger)
    
    @classmethod
    def from_environment(cls) -> 'RocketWelderClient':
        """
        Create client from CONNECTION_STRING environment variable
        
        Returns:
            RocketWelderClient instance
        """
        connection_string = os.environ.get("CONNECTION_STRING", "shm://default")
        return cls(connection_string)
    
    @property
    def connection(self) -> ConnectionString:
        """Get connection string configuration"""
        return self._connection
    
    @property
    def is_running(self) -> bool:
        """Check if client is running"""
        return self._running
    
    def on_frame(self, callback: Callable[[np.ndarray], None]) -> None:
        """
        Set callback for frame processing
        
        Args:
            callback: Function to call with each frame (receives np.ndarray)
        """
        if callback is None:
            raise ValueError("Callback cannot be None")
        self._frame_callback = callback
    
    def start(self) -> None:
        """Start frame processing"""
        if self._running:
            return
        
        if self._frame_callback is None:
            raise RuntimeError("Frame callback must be set before starting")
        
        self._running = True
        self._stop_event.clear()
        
        # Start processing thread based on protocol
        if self._connection.protocol == Protocol.SHM:
            self._thread = threading.Thread(target=self._process_shared_memory)
        elif self._connection.protocol == (Protocol.MJPEG | Protocol.HTTP):
            self._thread = threading.Thread(target=self._process_mjpeg_http)
        elif self._connection.protocol == (Protocol.MJPEG | Protocol.TCP):
            self._thread = threading.Thread(target=self._process_mjpeg_tcp)
        else:
            raise NotImplementedError(f"Protocol {self._connection.protocol} not supported")
        
        self._thread.daemon = True
        self._thread.start()
    
    def stop(self) -> None:
        """Stop frame processing"""
        if not self._running:
            return
        
        self._running = False
        self._stop_event.set()
        
        if self._thread:
            self._thread.join(timeout=5.0)
            self._thread = None
        
        if self._reader:
            self._reader.close()
            self._reader = None
        
        if self._writer:
            self._writer.close()
            self._writer = None
    
    def _process_shared_memory(self) -> None:
        """Process frames from shared memory (zero-copy)"""
        try:
            buffer_name = self._connection.buffer_name or "default"
            buffer_size = self._connection.buffer_size
            metadata_size = self._connection.metadata_size
            
            config = BufferConfig(
                metadata_size=metadata_size,
                payload_size=buffer_size
            )
            
            # Create reader - this creates the shared memory buffer
            self._reader = Reader(buffer_name, config, logger=self._logger)
            
            if self._connection.mode == "duplex":
                self._logger.warning("Duplex mode not fully implemented yet, operating in read-only mode")
            
            self._logger.debug(
                "Created shared memory buffer: %s (size: %d, metadata: %d)",
                buffer_name, buffer_size, metadata_size
            )
            
            while not self._stop_event.is_set():
                try:
                    # Read frame from shared memory (zero-copy)
                    frame = self._reader.read_frame(timeout=1.0)
                    
                    if frame is None or not frame.is_valid:
                        self._logger.info("No valid frame read, waiting for next frame")
                        continue
                    
                    # Use context manager for proper Frame disposal (RAII)
                    with frame:
                        # Parse metadata on first frame or when not yet parsed
                        if self._video_format is None:
                            self._parse_metadata()
                        
                        if self._video_format is None:
                            raise RuntimeError("No video format detected")
                        
                        # Create numpy array from frame data (zero-copy)
                        # frame.data is a memoryview that directly points to shared memory
                        mat = self._video_format.create_mat(frame.data)
                        
                        # Call frame callback with zero-copy array
                        self._frame_callback(mat)
                    
                except WriterDeadException:
                    self._logger.warning("Writer process died")
                    break
                except Exception as e:
                    self._logger.error("Error reading from shared memory: %s", e)
                    if not self._stop_event.wait(0.1):
                        continue
                    
        except Exception as e:
            self._logger.error("Error in shared memory processing: %s", e)
            raise
    
    def _parse_metadata(self) -> None:
        """Parse video format from metadata"""
        try:
            metadata_view = self._reader.get_metadata()
            if metadata_view is None or len(metadata_view) <= 4:
                return
            
            # The Python GetMetadata returns the metadata INCLUDING the 8-byte size prefix
            # We need to skip it to get to the GStreamer metadata
            
            # Skip first 8 bytes (uint64_t size prefix from ZeroBuffer Writer)
            # This is already done in the Python API - get_metadata() returns the actual metadata
            
            # Now read GStreamer's 4-byte size prefix (little-endian)
            json_size = struct.unpack('<I', bytes(metadata_view[:4]))[0]
            
            # Validate size
            if json_size == 0 or json_size > len(metadata_view) - 4:
                self._logger.warning(
                    "Invalid JSON size: %d (available: %d)",
                    json_size, len(metadata_view) - 4
                )
                return
            
            # Parse JSON metadata (skip the 4-byte GStreamer size prefix)
            json_bytes = bytes(metadata_view[4:4 + json_size])
            json_str = json_bytes.decode('utf-8')
            
            metadata = json.loads(json_str)
            
            # Try to parse from caps string first (most complete)
            if "caps" in metadata:
                caps = metadata["caps"]
                if caps:
                    self._video_format = GstCaps.parse(caps)
                    self._logger.info("Parsed video format from caps: %s", self._video_format)
                    return
            
            # Fallback to individual properties
            if "width" in metadata and "height" in metadata:
                width = metadata["width"]
                height = metadata["height"]
                format_str = metadata.get("format", "RGB")
                
                self._video_format = GstCaps.from_simple(width, height, format_str)
                self._logger.info("Parsed video format from properties: %s", self._video_format)
            
        except Exception as e:
            self._logger.warning("Failed to parse metadata: %s", e)
    
    def _process_mjpeg_http(self) -> None:
        """Process MJPEG stream over HTTP"""
        url = f"http://{self._connection.host}:{self._connection.port or 80}"
        if self._connection.path:
            url += f"/{self._connection.path}"
        self._process_mjpeg_stream(url)
    
    def _process_mjpeg_tcp(self) -> None:
        """Process MJPEG stream over TCP"""
        url = f"tcp://{self._connection.host}:{self._connection.port or 8080}"
        if self._connection.path:
            url += f"/{self._connection.path}"
        self._process_mjpeg_stream(url)
    
    def _process_mjpeg_stream(self, url: str) -> None:
        """Process MJPEG stream using OpenCV VideoCapture"""
        try:
            # Use OpenCV VideoCapture which can handle MJPEG streams directly
            cap = cv2.VideoCapture(url)
            
            if not cap.isOpened():
                raise ConnectionException(f"Failed to open video stream: {url}")
            
            self._logger.info("Opened MJPEG stream: %s", url)
            
            while not self._stop_event.is_set():
                try:
                    # Read frame from stream
                    ret, frame = cap.read()
                    
                    if ret and frame is not None:
                        # Process the frame
                        self._frame_callback(frame)
                    else:
                        # Small delay if no frame available
                        if self._stop_event.wait(0.01):
                            break
                    
                except Exception as e:
                    self._logger.error("Error reading from MJPEG stream: %s", e)
                    if self._stop_event.wait(0.1):
                        break
            
            cap.release()
            
        except Exception as e:
            self._logger.error("Error in MJPEG processing: %s", e)
            raise
    
    def __enter__(self):
        """Context manager entry"""
        return self
    
    def __exit__(self, exc_type, exc_val, exc_tb):
        """Context manager exit"""
        self.stop()
    
    def __del__(self):
        """Cleanup on deletion"""
        self.stop()