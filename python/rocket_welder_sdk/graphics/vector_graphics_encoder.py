"""
VectorGraphics Protocol V2 Encoder.

Matches C# VectorGraphicsEncoderV2 from BlazorBlaze.VectorGraphics.Protocol.

Wire format:
Message:
  [GlobalFrameId: 8 bytes LE]
  [LayerCount: 1 byte]
  For each layer:
    [LayerBlock...]
  [EndMarker: 0xFF 0xFF]

LayerBlock:
  [LayerId: 1 byte]
  [FrameType: 1 byte] // 0x00=Master, 0x01=Remain, 0x02=Clear
  If FrameType == Master:
    [OpCount: varint]
    [Operations...]
"""

from __future__ import annotations

import struct
from typing import Sequence, Tuple

from rocket_welder_sdk.varint import write_varint, write_zigzag

from .protocol import END_MARKER_BYTE1, END_MARKER_BYTE2, FrameType, OpType, PropertyId
from .rgb_color import RgbColor  # noqa: TC001 - used at runtime in method bodies


class VectorGraphicsEncoder:
    """
    Protocol V2 encoder for stateful canvas API with multi-layer support.

    Provides static methods for encoding VectorGraphics protocol messages.
    All methods write to a bytearray at a given offset and return bytes written.
    """

    @staticmethod
    def write_message_header(
        buffer: bytearray, offset: int, frame_id: int, layer_count: int
    ) -> int:
        """
        Write message header with frame ID and layer count.

        Args:
            buffer: Destination buffer
            offset: Starting offset
            frame_id: Frame identifier (64-bit unsigned)
            layer_count: Number of layers (0-255)

        Returns:
            Number of bytes written (9)
        """
        struct.pack_into("<Q", buffer, offset, frame_id)
        buffer[offset + 8] = layer_count
        return 9

    @staticmethod
    def write_end_marker(buffer: bytearray, offset: int) -> int:
        """
        Write end marker (0xFF 0xFF).

        Args:
            buffer: Destination buffer
            offset: Starting offset

        Returns:
            Number of bytes written (2)
        """
        buffer[offset] = END_MARKER_BYTE1
        buffer[offset + 1] = END_MARKER_BYTE2
        return 2

    # ============== Layer Block ==============

    @staticmethod
    def write_layer_master(buffer: bytearray, offset: int, layer_id: int, op_count: int) -> int:
        """
        Write layer block header for Master frame type.

        Args:
            buffer: Destination buffer
            offset: Starting offset
            layer_id: Layer ID (0-255)
            op_count: Number of operations

        Returns:
            Number of bytes written
        """
        buffer[offset] = layer_id
        buffer[offset + 1] = FrameType.MASTER
        return 2 + write_varint(buffer, offset + 2, op_count)

    @staticmethod
    def write_layer_remain(buffer: bytearray, offset: int, layer_id: int) -> int:
        """
        Write layer block for Remain frame type (keep previous content).

        Args:
            buffer: Destination buffer
            offset: Starting offset
            layer_id: Layer ID (0-255)

        Returns:
            Number of bytes written (2)
        """
        buffer[offset] = layer_id
        buffer[offset + 1] = FrameType.REMAIN
        return 2

    @staticmethod
    def write_layer_clear(buffer: bytearray, offset: int, layer_id: int) -> int:
        """
        Write layer block for Clear frame type (clear to transparent).

        Args:
            buffer: Destination buffer
            offset: Starting offset
            layer_id: Layer ID (0-255)

        Returns:
            Number of bytes written (2)
        """
        buffer[offset] = layer_id
        buffer[offset + 1] = FrameType.CLEAR
        return 2

    # ============== Context Operations - Styling ==============

    @staticmethod
    def write_set_stroke(buffer: bytearray, offset: int, color: RgbColor) -> int:
        """
        Write SetContext for stroke color.

        Args:
            buffer: Destination buffer
            offset: Starting offset
            color: Stroke color

        Returns:
            Number of bytes written (7)
        """
        buffer[offset] = OpType.SET_CONTEXT
        buffer[offset + 1] = 1  # 1 field
        buffer[offset + 2] = PropertyId.STROKE
        buffer[offset + 3] = color.r
        buffer[offset + 4] = color.g
        buffer[offset + 5] = color.b
        buffer[offset + 6] = color.a
        return 7

    @staticmethod
    def write_set_fill(buffer: bytearray, offset: int, color: RgbColor) -> int:
        """
        Write SetContext for fill color.

        Args:
            buffer: Destination buffer
            offset: Starting offset
            color: Fill color

        Returns:
            Number of bytes written (7)
        """
        buffer[offset] = OpType.SET_CONTEXT
        buffer[offset + 1] = 1
        buffer[offset + 2] = PropertyId.FILL
        buffer[offset + 3] = color.r
        buffer[offset + 4] = color.g
        buffer[offset + 5] = color.b
        buffer[offset + 6] = color.a
        return 7

    @staticmethod
    def write_set_thickness(buffer: bytearray, offset: int, thickness: int) -> int:
        """
        Write SetContext for stroke thickness.

        Args:
            buffer: Destination buffer
            offset: Starting offset
            thickness: Stroke thickness in pixels

        Returns:
            Number of bytes written
        """
        buffer[offset] = OpType.SET_CONTEXT
        buffer[offset + 1] = 1
        buffer[offset + 2] = PropertyId.THICKNESS
        return 3 + write_varint(buffer, offset + 3, thickness)

    @staticmethod
    def write_set_font_size(buffer: bytearray, offset: int, size: int) -> int:
        """
        Write SetContext for font size.

        Args:
            buffer: Destination buffer
            offset: Starting offset
            size: Font size in pixels

        Returns:
            Number of bytes written
        """
        buffer[offset] = OpType.SET_CONTEXT
        buffer[offset + 1] = 1
        buffer[offset + 2] = PropertyId.FONT_SIZE
        return 3 + write_varint(buffer, offset + 3, size)

    @staticmethod
    def write_set_font_color(buffer: bytearray, offset: int, color: RgbColor) -> int:
        """
        Write SetContext for font color.

        Args:
            buffer: Destination buffer
            offset: Starting offset
            color: Font color

        Returns:
            Number of bytes written (7)
        """
        buffer[offset] = OpType.SET_CONTEXT
        buffer[offset + 1] = 1
        buffer[offset + 2] = PropertyId.FONT_COLOR
        buffer[offset + 3] = color.r
        buffer[offset + 4] = color.g
        buffer[offset + 5] = color.b
        buffer[offset + 6] = color.a
        return 7

    # ============== Context Operations - Transforms ==============

    @staticmethod
    def write_set_offset(buffer: bytearray, offset: int, x: float, y: float) -> int:
        """
        Write SetContext for translation offset.

        Args:
            buffer: Destination buffer
            offset: Starting offset
            x: X translation
            y: Y translation

        Returns:
            Number of bytes written
        """
        buffer[offset] = OpType.SET_CONTEXT
        buffer[offset + 1] = 1
        buffer[offset + 2] = PropertyId.OFFSET
        pos = offset + 3
        pos += write_zigzag(buffer, pos, int(x))
        pos += write_zigzag(buffer, pos, int(y))
        return pos - offset

    @staticmethod
    def write_set_rotation(buffer: bytearray, offset: int, degrees: float) -> int:
        """
        Write SetContext for rotation in degrees.

        Args:
            buffer: Destination buffer
            offset: Starting offset
            degrees: Rotation angle in degrees

        Returns:
            Number of bytes written (7)
        """
        buffer[offset] = OpType.SET_CONTEXT
        buffer[offset + 1] = 1
        buffer[offset + 2] = PropertyId.ROTATION
        struct.pack_into("<f", buffer, offset + 3, degrees)
        return 7

    @staticmethod
    def write_set_scale(buffer: bytearray, offset: int, scale_x: float, scale_y: float) -> int:
        """
        Write SetContext for scale.

        Args:
            buffer: Destination buffer
            offset: Starting offset
            scale_x: X scale factor
            scale_y: Y scale factor

        Returns:
            Number of bytes written (11)
        """
        buffer[offset] = OpType.SET_CONTEXT
        buffer[offset + 1] = 1
        buffer[offset + 2] = PropertyId.SCALE
        struct.pack_into("<ff", buffer, offset + 3, scale_x, scale_y)
        return 11

    @staticmethod
    def write_set_skew(buffer: bytearray, offset: int, skew_x: float, skew_y: float) -> int:
        """
        Write SetContext for skew.

        Args:
            buffer: Destination buffer
            offset: Starting offset
            skew_x: X skew factor
            skew_y: Y skew factor

        Returns:
            Number of bytes written (11)
        """
        buffer[offset] = OpType.SET_CONTEXT
        buffer[offset + 1] = 1
        buffer[offset + 2] = PropertyId.SKEW
        struct.pack_into("<ff", buffer, offset + 3, skew_x, skew_y)
        return 11

    @staticmethod
    def write_set_matrix(
        buffer: bytearray,
        offset: int,
        scale_x: float,
        skew_x: float,
        trans_x: float,
        skew_y: float,
        scale_y: float,
        trans_y: float,
    ) -> int:
        """
        Write SetContext for full transformation matrix (6 floats).

        Matrix layout matches SKMatrix:
        | ScaleX  SkewX   TransX |
        | SkewY   ScaleY  TransY |
        | Persp0  Persp1  Persp2 |  (not sent, assumed identity)

        Args:
            buffer: Destination buffer
            offset: Starting offset
            scale_x, skew_x, trans_x: First row
            skew_y, scale_y, trans_y: Second row

        Returns:
            Number of bytes written (27)
        """
        buffer[offset] = OpType.SET_CONTEXT
        buffer[offset + 1] = 1
        buffer[offset + 2] = PropertyId.MATRIX
        struct.pack_into(
            "<ffffff", buffer, offset + 3, scale_x, skew_x, trans_x, skew_y, scale_y, trans_y
        )
        return 27

    # ============== Context Stack ==============

    @staticmethod
    def write_save_context(buffer: bytearray, offset: int) -> int:
        """
        Write SaveContext operation.

        Args:
            buffer: Destination buffer
            offset: Starting offset

        Returns:
            Number of bytes written (1)
        """
        buffer[offset] = OpType.SAVE_CONTEXT
        return 1

    @staticmethod
    def write_restore_context(buffer: bytearray, offset: int) -> int:
        """
        Write RestoreContext operation.

        Args:
            buffer: Destination buffer
            offset: Starting offset

        Returns:
            Number of bytes written (1)
        """
        buffer[offset] = OpType.RESTORE_CONTEXT
        return 1

    @staticmethod
    def write_reset_context(buffer: bytearray, offset: int) -> int:
        """
        Write ResetContext operation.

        Args:
            buffer: Destination buffer
            offset: Starting offset

        Returns:
            Number of bytes written (1)
        """
        buffer[offset] = OpType.RESET_CONTEXT
        return 1

    # ============== Draw Operations ==============

    @staticmethod
    def write_draw_polygon(
        buffer: bytearray, offset: int, points: Sequence[Tuple[float, float]]
    ) -> int:
        """
        Write DrawPolygon operation with delta-encoded points.

        Args:
            buffer: Destination buffer
            offset: Starting offset
            points: Sequence of (x, y) tuples

        Returns:
            Number of bytes written
        """
        pos = offset
        buffer[pos] = OpType.DRAW_POLYGON
        pos += 1
        pos += write_varint(buffer, pos, len(points))

        if len(points) > 0:
            # First point - absolute
            first_x = int(points[0][0])
            first_y = int(points[0][1])
            pos += write_zigzag(buffer, pos, first_x)
            pos += write_zigzag(buffer, pos, first_y)

            # Subsequent points - delta encoded
            last_x = first_x
            last_y = first_y
            for i in range(1, len(points)):
                x = int(points[i][0])
                y = int(points[i][1])
                pos += write_zigzag(buffer, pos, x - last_x)
                pos += write_zigzag(buffer, pos, y - last_y)
                last_x = x
                last_y = y

        return pos - offset

    @staticmethod
    def write_draw_text(buffer: bytearray, offset: int, text: str, x: int, y: int) -> int:
        """
        Write DrawText operation.

        Args:
            buffer: Destination buffer
            offset: Starting offset
            text: Text to draw
            x: X position
            y: Y position

        Returns:
            Number of bytes written
        """
        pos = offset
        buffer[pos] = OpType.DRAW_TEXT
        pos += 1
        pos += write_zigzag(buffer, pos, x)
        pos += write_zigzag(buffer, pos, y)

        text_bytes = text.encode("utf-8")
        pos += write_varint(buffer, pos, len(text_bytes))
        buffer[pos : pos + len(text_bytes)] = text_bytes
        pos += len(text_bytes)

        return pos - offset

    @staticmethod
    def write_draw_circle(
        buffer: bytearray, offset: int, center_x: int, center_y: int, radius: int
    ) -> int:
        """
        Write DrawCircle operation.

        Args:
            buffer: Destination buffer
            offset: Starting offset
            center_x: Center X coordinate
            center_y: Center Y coordinate
            radius: Circle radius

        Returns:
            Number of bytes written
        """
        pos = offset
        buffer[pos] = OpType.DRAW_CIRCLE
        pos += 1
        pos += write_zigzag(buffer, pos, center_x)
        pos += write_zigzag(buffer, pos, center_y)
        pos += write_varint(buffer, pos, radius)
        return pos - offset

    @staticmethod
    def write_draw_rect(
        buffer: bytearray, offset: int, x: int, y: int, width: int, height: int
    ) -> int:
        """
        Write DrawRect operation.

        Args:
            buffer: Destination buffer
            offset: Starting offset
            x: X position
            y: Y position
            width: Rectangle width
            height: Rectangle height

        Returns:
            Number of bytes written
        """
        pos = offset
        buffer[pos] = OpType.DRAW_RECT
        pos += 1
        pos += write_zigzag(buffer, pos, x)
        pos += write_zigzag(buffer, pos, y)
        pos += write_varint(buffer, pos, width)
        pos += write_varint(buffer, pos, height)
        return pos - offset

    @staticmethod
    def write_draw_line(buffer: bytearray, offset: int, x1: int, y1: int, x2: int, y2: int) -> int:
        """
        Write DrawLine operation.

        Args:
            buffer: Destination buffer
            offset: Starting offset
            x1, y1: Start point
            x2, y2: End point

        Returns:
            Number of bytes written
        """
        pos = offset
        buffer[pos] = OpType.DRAW_LINE
        pos += 1
        pos += write_zigzag(buffer, pos, x1)
        pos += write_zigzag(buffer, pos, y1)
        pos += write_zigzag(buffer, pos, x2)
        pos += write_zigzag(buffer, pos, y2)
        return pos - offset

    @staticmethod
    def write_draw_jpeg(
        buffer: bytearray, offset: int, jpeg_data: bytes, x: int, y: int, width: int, height: int
    ) -> int:
        """
        Write DrawJpeg operation with raw JPEG data.

        Args:
            buffer: Destination buffer
            offset: Starting offset
            jpeg_data: Raw JPEG bytes
            x: X position
            y: Y position
            width: Display width
            height: Display height

        Returns:
            Number of bytes written
        """
        pos = offset
        buffer[pos] = OpType.DRAW_JPEG
        pos += 1
        pos += write_zigzag(buffer, pos, x)
        pos += write_zigzag(buffer, pos, y)
        pos += write_varint(buffer, pos, width)
        pos += write_varint(buffer, pos, height)
        pos += write_varint(buffer, pos, len(jpeg_data))
        buffer[pos : pos + len(jpeg_data)] = jpeg_data
        pos += len(jpeg_data)
        return pos - offset
