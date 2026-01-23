"""
ILayerCanvas protocol - per-layer drawing interface.

Matches C# ILayerCanvas interface from BlazorBlaze.Server.
"""

from __future__ import annotations

from typing import TYPE_CHECKING, Protocol, Sequence, Tuple, runtime_checkable

if TYPE_CHECKING:
    from .rgb_color import RgbColor


@runtime_checkable
class ILayerCanvas(Protocol):
    """
    Represents a single rendering layer with stateful context management.

    Mirrors SkiaSharp's canvas API for familiar usage patterns.
    This is a Protocol class (interface) matching C# ILayerCanvas.
    """

    @property
    def layer_id(self) -> int:
        """The layer ID (z-order index)."""
        ...

    # ============== Layer Frame Type ==============

    def master(self) -> None:
        """
        Sets this layer to Master mode - clears and redraws with operations that follow.

        This is the default mode when drawing operations are added.
        """
        ...

    def remain(self) -> None:
        """
        Sets this layer to Remain mode - keeps previous content unchanged.

        No operations are sent for this layer, saving bandwidth.
        """
        ...

    def clear(self) -> None:
        """
        Sets this layer to Clear mode - clears to transparent with no redraw.

        Use when you want to hide a layer without drawing new content.
        """
        ...

    # ============== Context State - Styling ==============

    def set_stroke(self, color: RgbColor) -> None:
        """Sets the stroke color for subsequent draw operations."""
        ...

    def set_fill(self, color: RgbColor) -> None:
        """Sets the fill color for subsequent draw operations."""
        ...

    def set_thickness(self, width: int) -> None:
        """Sets the stroke thickness in pixels."""
        ...

    def set_font_size(self, size: int) -> None:
        """Sets the font size in pixels."""
        ...

    def set_font_color(self, color: RgbColor) -> None:
        """Sets the font color for text operations."""
        ...

    # ============== Context State - Transforms ==============

    def translate(self, dx: float, dy: float) -> None:
        """Sets the translation offset for subsequent draw operations."""
        ...

    def rotate(self, degrees: float) -> None:
        """Sets the rotation in degrees for subsequent draw operations."""
        ...

    def scale(self, sx: float, sy: float) -> None:
        """Sets the scale factors for subsequent draw operations."""
        ...

    def skew(self, kx: float, ky: float) -> None:
        """Sets the skew factors for subsequent draw operations."""
        ...

    def set_matrix(
        self,
        scale_x: float,
        skew_x: float,
        trans_x: float,
        skew_y: float,
        scale_y: float,
        trans_y: float,
    ) -> None:
        """
        Sets a full transformation matrix for subsequent draw operations.

        Takes precedence over individual transform properties.

        Matrix layout matches SKMatrix:
        | ScaleX  SkewX   TransX |
        | SkewY   ScaleY  TransY |
        """
        ...

    # ============== Context Stack ==============

    def save(self) -> None:
        """
        Pushes the current context state onto a stack.

        Use with restore() for hierarchical transforms.
        """
        ...

    def restore(self) -> None:
        """Pops and restores the most recently saved context state."""
        ...

    def reset_context(self) -> None:
        """Resets the context to default values (black stroke, identity transform)."""
        ...

    # ============== Draw Operations ==============

    def draw_polygon(self, points: Sequence[Tuple[float, float]]) -> None:
        """Draws a polygon using the current context state."""
        ...

    def draw_text(self, text: str, x: int, y: int) -> None:
        """Draws text at the specified position using the current context state."""
        ...

    def draw_circle(self, center_x: int, center_y: int, radius: int) -> None:
        """Draws a circle using the current context state."""
        ...

    def draw_rectangle(self, x: int, y: int, width: int, height: int) -> None:
        """Draws a rectangle using the current context state."""
        ...

    def draw_line(self, x1: int, y1: int, x2: int, y2: int) -> None:
        """Draws a line using the current context state."""
        ...

    def draw_jpeg(self, jpeg_data: bytes, x: int, y: int, width: int, height: int) -> None:
        """Draws a JPEG image at the specified position and size."""
        ...
