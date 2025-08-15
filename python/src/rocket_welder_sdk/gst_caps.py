"""
GStreamer caps parsing for video format information

Mirrors the C# implementation with Python idioms.
"""

from dataclasses import dataclass
from typing import Optional, Tuple
import re
import numpy as np
import cv2


@dataclass(frozen=True)
class GstCaps:
    """
    Readonly GStreamer caps configuration
    
    Mirrors C# readonly record struct with IParsable interface.
    """
    width: int
    height: int
    format: str
    framerate: Optional[Tuple[int, int]] = None
    
    @classmethod
    def parse(cls, s: str, provider=None) -> 'GstCaps':
        """
        Parse GStreamer caps string (equivalent to IParsable<T>.Parse in C#)
        
        Args:
            s: Caps string like "video/x-raw,format=RGB,width=640,height=480,framerate=30/1"
            provider: Not used, kept for API compatibility with C#
        
        Returns:
            GstCaps instance
        
        Raises:
            ValueError: If caps format is invalid
        """
        if not s:
            raise ValueError("Caps string cannot be empty")
        
        # Remove video/x-raw prefix if present
        if s.startswith("video/x-raw"):
            s = s[len("video/x-raw"):].lstrip(",")
        
        # Parse key=value pairs
        params = {}
        for part in s.split(","):
            if "=" in part:
                key, value = part.split("=", 1)
                params[key.strip()] = value.strip()
        
        # Extract required fields
        if "width" not in params:
            raise ValueError("Missing 'width' in caps")
        if "height" not in params:
            raise ValueError("Missing 'height' in caps")
        
        # Handle GStreamer type annotations like "(int)640"
        width_str = params["width"]
        if width_str.startswith("(int)"):
            width_str = width_str[5:]
        width = int(width_str)
        
        height_str = params["height"]
        if height_str.startswith("(int)"):
            height_str = height_str[5:]
        height = int(height_str)
        format_str = params.get("format", "RGB")
        
        # Parse framerate if present
        framerate = None
        if "framerate" in params:
            framerate_str = params["framerate"]
            # Handle GStreamer type annotations like "(fraction)30/1"
            if framerate_str.startswith("(fraction)"):
                framerate_str = framerate_str[10:]
            fr_match = re.match(r"(\d+)/(\d+)", framerate_str)
            if fr_match:
                framerate = (int(fr_match.group(1)), int(fr_match.group(2)))
        
        return cls(
            width=width,
            height=height,
            format=format_str,
            framerate=framerate
        )
    
    @classmethod
    def try_parse(cls, s: str, provider=None) -> tuple[bool, Optional['GstCaps']]:
        """
        Try to parse caps string (equivalent to IParsable<T>.TryParse in C#)
        
        Args:
            s: Caps string to parse
            provider: Not used, kept for API compatibility
        
        Returns:
            Tuple of (success, GstCaps or None)
        """
        try:
            result = cls.parse(s, provider)
            return True, result
        except (ValueError, KeyError):
            return False, None
    
    @classmethod
    def from_simple(cls, width: int, height: int, format_str: str = "RGB") -> 'GstCaps':
        """
        Create from simple parameters
        
        Args:
            width: Frame width
            height: Frame height
            format_str: Pixel format (RGB, BGR, GRAY8, etc.)
        
        Returns:
            GstCaps instance
        """
        return cls(width=width, height=height, format=format_str)
    
    def get_opencv_dtype(self) -> int:
        """
        Get OpenCV data type for the format
        
        Returns:
            OpenCV dtype constant
        """
        # Map GStreamer formats to OpenCV types
        format_map = {
            "RGB": cv2.CV_8UC3,
            "BGR": cv2.CV_8UC3,
            "RGBA": cv2.CV_8UC4,
            "BGRA": cv2.CV_8UC4,
            "GRAY8": cv2.CV_8UC1,
            "GRAY16_LE": cv2.CV_16UC1,
            "GRAY16_BE": cv2.CV_16UC1,
        }
        
        return format_map.get(self.format, cv2.CV_8UC3)
    
    def get_channels(self) -> int:
        """
        Get number of channels for the format
        
        Returns:
            Number of channels
        """
        if self.format in ["RGB", "BGR"]:
            return 3
        elif self.format in ["RGBA", "BGRA"]:
            return 4
        elif self.format.startswith("GRAY"):
            return 1
        else:
            return 3  # Default to 3 channels
    
    def get_numpy_dtype(self) -> np.dtype:
        """
        Get NumPy data type for the format
        
        Returns:
            NumPy dtype
        """
        if "16" in self.format:
            return np.dtype(np.uint16)
        else:
            return np.dtype(np.uint8)
    
    def create_mat(self, data_ptr: memoryview) -> np.ndarray:
        """
        Create OpenCV Mat from data pointer without copying (zero-copy)
        
        Args:
            data_ptr: Memory view of the frame data
        
        Returns:
            NumPy array that wraps the data (no copy)
        """
        # Calculate expected size
        channels = self.get_channels()
        dtype = self.get_numpy_dtype()
        expected_size = self.width * self.height * channels * dtype.itemsize
        
        # Create numpy array from memoryview (zero-copy)
        # The memoryview directly points to shared memory
        flat_array = np.frombuffer(data_ptr, dtype=dtype, count=self.width * self.height * channels)
        
        # Reshape to image dimensions
        if channels == 1:
            return flat_array.reshape((self.height, self.width))
        else:
            return flat_array.reshape((self.height, self.width, channels))
    
    def create_mat_from_buffer(self, buffer: bytes) -> np.ndarray:
        """
        Create OpenCV Mat from byte buffer (makes a copy)
        
        Args:
            buffer: Byte buffer containing frame data
        
        Returns:
            NumPy array (copy of data)
        """
        channels = self.get_channels()
        dtype = self.get_numpy_dtype()
        
        # Create numpy array from bytes
        flat_array = np.frombuffer(buffer, dtype=dtype)
        
        # Reshape to image dimensions
        if channels == 1:
            return flat_array.reshape((self.height, self.width))
        else:
            return flat_array.reshape((self.height, self.width, channels))
    
    def __str__(self) -> str:
        """String representation as GStreamer caps"""
        caps = f"video/x-raw,format={self.format},width={self.width},height={self.height}"
        if self.framerate:
            caps += f",framerate={self.framerate[0]}/{self.framerate[1]}"
        return caps