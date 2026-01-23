"""
Stage sink and writer for VectorGraphics streaming.

Matches C# IStageSink, IStageWriter, StageSink, StageWriter
from RocketWelder.SDK.Graphics.
"""

from __future__ import annotations

from typing import TYPE_CHECKING, Dict, List, Protocol, Sequence, Tuple, runtime_checkable

from rocket_welder_sdk.transport.frame_sink import IFrameSink  # noqa: TC001 - used at runtime

from .protocol import FrameType
from .rgb_color import RgbColor  # noqa: TC001 - used at runtime in method bodies
from .vector_graphics_encoder import VectorGraphicsEncoder

if TYPE_CHECKING:
    from .layer_canvas import ILayerCanvas


@runtime_checkable
class IStageWriter(Protocol):
    """
    Stage writer for vector graphics overlays.

    User-facing API only - auto-flushes on close like other writers.
    Matches C# IStageWriter interface.

    Example:
        with stage_sink.create_writer(frame_id) as writer:
            # Draw on layer 0 (background)
            writer[0].set_stroke(RgbColor.Red)
            writer[0].draw_polygon(contour_points)

            # Draw on layer 1 (labels)
            writer[1].draw_text(f"Frame: {writer.frame_id}", 10, 20)
        # writer auto-flushes on context exit
    """

    @property
    def frame_id(self) -> int:
        """Gets the current frame ID."""
        ...

    def __getitem__(self, layer_id: int) -> ILayerCanvas:
        """
        Gets the layer canvas for the specified layer ID.

        Layers are composited with lower IDs at the back (0 = bottom).

        Args:
            layer_id: Layer ID (0-15)

        Returns:
            The layer canvas for drawing operations
        """
        ...

    def layer(self, layer_id: int) -> ILayerCanvas:
        """
        Gets the layer canvas for the specified layer ID.

        Alternative method syntax for the indexer.

        Args:
            layer_id: Layer ID (0-15)

        Returns:
            The layer canvas for drawing operations
        """
        ...

    def close(self) -> None:
        """Flushes and closes the writer."""
        ...

    def __enter__(self) -> IStageWriter:
        """Context manager entry."""
        ...

    def __exit__(self, exc_type: object, exc_val: object, exc_tb: object) -> None:
        """Context manager exit - auto-flushes."""
        ...


@runtime_checkable
class IStageSink(Protocol):
    """
    Factory for creating per-frame stage writers (transport-agnostic).

    Follows the same pattern as ISegmentationResultSink and IKeyPointsSink.
    Matches C# IStageSink interface.
    """

    def create_writer(self, frame_id: int) -> IStageWriter:
        """
        Creates a writer for the specified frame.

        The writer auto-flushes on close.

        Args:
            frame_id: Frame identifier

        Returns:
            Stage writer that auto-flushes on close
        """
        ...

    def close(self) -> None:
        """Closes the sink and releases resources."""
        ...


class LayerEncoder:
    """
    Internal layer encoder that writes operations directly to buffer.

    Implements ILayerCanvas protocol by encoding operations to binary.
    """

    __slots__ = (
        "_buffer",
        "_frame_type",
        "_header_reserve",
        "_layer_id",
        "_offset",
        "_operation_count",
    )

    # Reserve space for layer header at start of buffer
    HEADER_RESERVE = 16
    LAYER_BUFFER_SIZE = 256 * 1024  # 256KB per layer to accommodate JPEG frames

    def __init__(self, layer_id: int) -> None:
        """
        Creates a new layer encoder.

        Args:
            layer_id: Layer ID (0-15)
        """
        self._layer_id = layer_id
        self._frame_type = FrameType.MASTER
        self._buffer = bytearray(self.LAYER_BUFFER_SIZE)
        self._offset = self.HEADER_RESERVE
        self._operation_count = 0
        self._header_reserve = self.HEADER_RESERVE

    @property
    def layer_id(self) -> int:
        """The layer ID."""
        return self._layer_id

    def copy_encoded_data(self, dest_buffer: bytearray, dest_offset: int) -> int:
        """
        Copies the encoded layer data (with header) to the destination buffer.

        Args:
            dest_buffer: Destination buffer
            dest_offset: Starting offset in destination

        Returns:
            Number of bytes written
        """
        pos = dest_offset

        if self._frame_type == FrameType.MASTER:
            pos += VectorGraphicsEncoder.write_layer_master(
                dest_buffer, pos, self._layer_id, self._operation_count
            )
            data_length = self._offset - self._header_reserve
            dest_buffer[pos : pos + data_length] = self._buffer[self._header_reserve : self._offset]
            pos += data_length
        elif self._frame_type == FrameType.REMAIN:
            pos += VectorGraphicsEncoder.write_layer_remain(dest_buffer, pos, self._layer_id)
        elif self._frame_type == FrameType.CLEAR:
            pos += VectorGraphicsEncoder.write_layer_clear(dest_buffer, pos, self._layer_id)

        return pos - dest_offset

    # ============== Frame Type ==============

    def master(self) -> None:
        """Sets this layer to Master mode."""
        self._frame_type = FrameType.MASTER

    def remain(self) -> None:
        """Sets this layer to Remain mode."""
        self._frame_type = FrameType.REMAIN

    def clear(self) -> None:
        """Sets this layer to Clear mode."""
        self._frame_type = FrameType.CLEAR

    # ============== Context State - Styling ==============

    def set_stroke(self, color: RgbColor) -> None:
        """Sets the stroke color."""
        self._offset += VectorGraphicsEncoder.write_set_stroke(self._buffer, self._offset, color)
        self._operation_count += 1

    def set_fill(self, color: RgbColor) -> None:
        """Sets the fill color."""
        self._offset += VectorGraphicsEncoder.write_set_fill(self._buffer, self._offset, color)
        self._operation_count += 1

    def set_thickness(self, width: int) -> None:
        """Sets the stroke thickness."""
        self._offset += VectorGraphicsEncoder.write_set_thickness(self._buffer, self._offset, width)
        self._operation_count += 1

    def set_font_size(self, size: int) -> None:
        """Sets the font size."""
        self._offset += VectorGraphicsEncoder.write_set_font_size(self._buffer, self._offset, size)
        self._operation_count += 1

    def set_font_color(self, color: RgbColor) -> None:
        """Sets the font color."""
        self._offset += VectorGraphicsEncoder.write_set_font_color(
            self._buffer, self._offset, color
        )
        self._operation_count += 1

    # ============== Context State - Transforms ==============

    def translate(self, dx: float, dy: float) -> None:
        """Sets the translation offset."""
        self._offset += VectorGraphicsEncoder.write_set_offset(self._buffer, self._offset, dx, dy)
        self._operation_count += 1

    def rotate(self, degrees: float) -> None:
        """Sets the rotation."""
        self._offset += VectorGraphicsEncoder.write_set_rotation(
            self._buffer, self._offset, degrees
        )
        self._operation_count += 1

    def scale(self, sx: float, sy: float) -> None:
        """Sets the scale."""
        self._offset += VectorGraphicsEncoder.write_set_scale(self._buffer, self._offset, sx, sy)
        self._operation_count += 1

    def skew(self, kx: float, ky: float) -> None:
        """Sets the skew."""
        self._offset += VectorGraphicsEncoder.write_set_skew(self._buffer, self._offset, kx, ky)
        self._operation_count += 1

    def set_matrix(
        self,
        scale_x: float,
        skew_x: float,
        trans_x: float,
        skew_y: float,
        scale_y: float,
        trans_y: float,
    ) -> None:
        """Sets the transformation matrix."""
        self._offset += VectorGraphicsEncoder.write_set_matrix(
            self._buffer, self._offset, scale_x, skew_x, trans_x, skew_y, scale_y, trans_y
        )
        self._operation_count += 1

    # ============== Context Stack ==============

    def save(self) -> None:
        """Pushes the current context state."""
        self._offset += VectorGraphicsEncoder.write_save_context(self._buffer, self._offset)
        self._operation_count += 1

    def restore(self) -> None:
        """Pops and restores the context state."""
        self._offset += VectorGraphicsEncoder.write_restore_context(self._buffer, self._offset)
        self._operation_count += 1

    def reset_context(self) -> None:
        """Resets the context to defaults."""
        self._offset += VectorGraphicsEncoder.write_reset_context(self._buffer, self._offset)
        self._operation_count += 1

    # ============== Draw Operations ==============

    def draw_polygon(self, points: Sequence[Tuple[float, float]]) -> None:
        """Draws a polygon."""
        self._offset += VectorGraphicsEncoder.write_draw_polygon(self._buffer, self._offset, points)
        self._operation_count += 1

    def draw_text(self, text: str, x: int, y: int) -> None:
        """Draws text."""
        self._offset += VectorGraphicsEncoder.write_draw_text(
            self._buffer, self._offset, text, x, y
        )
        self._operation_count += 1

    def draw_circle(self, center_x: int, center_y: int, radius: int) -> None:
        """Draws a circle."""
        self._offset += VectorGraphicsEncoder.write_draw_circle(
            self._buffer, self._offset, center_x, center_y, radius
        )
        self._operation_count += 1

    def draw_rectangle(self, x: int, y: int, width: int, height: int) -> None:
        """Draws a rectangle."""
        self._offset += VectorGraphicsEncoder.write_draw_rect(
            self._buffer, self._offset, x, y, width, height
        )
        self._operation_count += 1

    def draw_line(self, x1: int, y1: int, x2: int, y2: int) -> None:
        """Draws a line."""
        self._offset += VectorGraphicsEncoder.write_draw_line(
            self._buffer, self._offset, x1, y1, x2, y2
        )
        self._operation_count += 1

    def draw_jpeg(self, jpeg_data: bytes, x: int, y: int, width: int, height: int) -> None:
        """Draws a JPEG image."""
        self._offset += VectorGraphicsEncoder.write_draw_jpeg(
            self._buffer, self._offset, jpeg_data, x, y, width, height
        )
        self._operation_count += 1


class StageWriter:
    """
    Per-frame stage writer that auto-flushes on close.

    Follows the same pattern as SegmentationResultWriter and KeyPointsWriter.
    Implements IStageWriter protocol.
    """

    __slots__ = ("_active_layer_ids", "_buffer", "_closed", "_frame_id", "_frame_sink", "_layers")

    DEFAULT_BUFFER_SIZE = 1024 * 1024  # 1MB

    def __init__(
        self, frame_id: int, frame_sink: IFrameSink, buffer_size: int = DEFAULT_BUFFER_SIZE
    ) -> None:
        """
        Creates a new stage writer.

        Args:
            frame_id: Frame identifier
            frame_sink: Transport for sending encoded frames
            buffer_size: Size of the encoding buffer (default 1MB)
        """
        self._frame_id = frame_id
        self._frame_sink = frame_sink
        self._buffer = bytearray(buffer_size)
        self._layers: Dict[int, LayerEncoder] = {}
        self._active_layer_ids: List[int] = []
        self._closed = False

    @property
    def frame_id(self) -> int:
        """Gets the frame ID for this writer."""
        return self._frame_id

    def __getitem__(self, layer_id: int) -> LayerEncoder:
        """Gets the layer canvas for the specified layer ID."""
        return self.layer(layer_id)

    def layer(self, layer_id: int) -> LayerEncoder:
        """
        Gets the layer canvas for the specified layer ID.

        Args:
            layer_id: Layer ID (0-15)

        Returns:
            The layer encoder for drawing operations
        """
        if self._closed:
            raise RuntimeError("StageWriter is closed")

        if layer_id not in self._layers:
            self._layers[layer_id] = LayerEncoder(layer_id)

        # Track that this layer was accessed
        if layer_id not in self._active_layer_ids:
            self._active_layer_ids.append(layer_id)

        return self._layers[layer_id]

    def _flush(self) -> None:
        """
        Encodes and sends all layer operations via transport.

        Called automatically on close.
        """
        if not self._active_layer_ids:
            return

        offset = 0

        # Write message header
        offset += VectorGraphicsEncoder.write_message_header(
            self._buffer, offset, self._frame_id, len(self._active_layer_ids)
        )

        # Encode each active layer
        for layer_id in self._active_layer_ids:
            layer = self._layers[layer_id]
            offset += layer.copy_encoded_data(self._buffer, offset)

        # Write end marker
        offset += VectorGraphicsEncoder.write_end_marker(self._buffer, offset)

        # Send via transport
        self._frame_sink.write_frame(bytes(self._buffer[:offset]))

    def close(self) -> None:
        """Flushes and closes the writer."""
        if self._closed:
            return
        self._closed = True

        # Auto-flush on close (same pattern as other writers)
        self._flush()

        # Clear state
        self._layers.clear()
        self._active_layer_ids.clear()

    def __enter__(self) -> StageWriter:
        """Context manager entry."""
        return self

    def __exit__(self, exc_type: object, exc_val: object, exc_tb: object) -> None:
        """Context manager exit - auto-flushes."""
        self.close()


class StageSink:
    """
    Factory for creating per-frame stage writers.

    Follows the same pattern as SegmentationResultSink and KeyPointsSink.
    Implements IStageSink protocol.
    """

    __slots__ = ("_buffer_size", "_closed", "_frame_sink", "_owns_sink")

    def __init__(
        self,
        frame_sink: IFrameSink,
        buffer_size: int = StageWriter.DEFAULT_BUFFER_SIZE,
        owns_sink: bool = True,
    ) -> None:
        """
        Creates a StageSink with the specified transport.

        Args:
            frame_sink: The transport for sending encoded frames
            buffer_size: Size of the encoding buffer per writer (default 1MB)
            owns_sink: If True, closes the sink when this factory is closed
        """
        self._frame_sink = frame_sink
        self._buffer_size = buffer_size
        self._owns_sink = owns_sink
        self._closed = False

    def create_writer(self, frame_id: int) -> StageWriter:
        """
        Creates a writer for the specified frame.

        The writer auto-flushes on close.

        Args:
            frame_id: Frame identifier

        Returns:
            Stage writer that auto-flushes on close
        """
        if self._closed:
            raise RuntimeError("StageSink is closed")

        return StageWriter(frame_id, self._frame_sink, self._buffer_size)

    def close(self) -> None:
        """Closes the sink and releases resources."""
        if self._closed:
            return
        self._closed = True

        if self._owns_sink:
            self._frame_sink.close()

    def __enter__(self) -> StageSink:
        """Context manager entry."""
        return self

    def __exit__(self, exc_type: object, exc_val: object, exc_tb: object) -> None:
        """Context manager exit."""
        self.close()
