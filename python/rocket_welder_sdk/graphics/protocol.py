"""
Protocol V2 enums and constants for VectorGraphics encoding.

Matches C# ProtocolV2 from BlazorBlaze.VectorGraphics.Protocol.
"""

from __future__ import annotations

from enum import IntEnum


class FrameType(IntEnum):
    """
    Frame type for layer updates.

    Matches C# FrameType enum.
    """

    MASTER = 0x00  # Clear and redraw with operations
    REMAIN = 0x01  # Keep previous content unchanged
    CLEAR = 0x02  # Clear to transparent with no redraw


class OpType(IntEnum):
    """
    Operation type codes.

    Matches C# OpType enum.
    """

    # Draw operations (0x01-0x0F)
    DRAW_POLYGON = 0x01
    DRAW_TEXT = 0x02
    DRAW_CIRCLE = 0x03
    DRAW_RECT = 0x04
    DRAW_LINE = 0x05
    DRAW_JPEG = 0x07

    # Context operations (0x10-0x1F)
    SET_CONTEXT = 0x10
    SAVE_CONTEXT = 0x11
    RESTORE_CONTEXT = 0x12
    RESET_CONTEXT = 0x13


class PropertyId(IntEnum):
    """
    Property IDs for SetContext operation.

    Matches C# PropertyId enum.
    """

    # Styling properties (0x01-0x0F)
    STROKE = 0x01
    FILL = 0x02
    THICKNESS = 0x03
    FONT_SIZE = 0x04
    FONT_COLOR = 0x05

    # Transform properties (0x10-0x1F)
    OFFSET = 0x10
    ROTATION = 0x11
    SCALE = 0x12
    SKEW = 0x13

    # Matrix (0x20)
    MATRIX = 0x20


# Protocol constants
END_MARKER_BYTE1: int = 0xFF
END_MARKER_BYTE2: int = 0xFF
