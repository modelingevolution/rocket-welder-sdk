"""
VectorGraphics module for streaming graphics overlays.

This module provides classes for encoding and streaming vector graphics
to the browser using Protocol V2.

Example:
    from rocket_welder_sdk.graphics import StageSink, RgbColor
    from rocket_welder_sdk.transport import UnixSocketFrameSink

    # Create transport and sink
    sink = UnixSocketFrameSink("/tmp/graphics.sock")
    stage = StageSink(sink)

    # Draw graphics for each frame
    with stage.create_writer(frame_id) as writer:
        writer[0].set_stroke(RgbColor.Red)
        writer[0].draw_polygon([(0, 0), (100, 0), (100, 100)])
        writer[1].draw_text("Hello", 10, 20)
"""

from .layer_canvas import ILayerCanvas
from .protocol import END_MARKER_BYTE1, END_MARKER_BYTE2, FrameType, OpType, PropertyId
from .rgb_color import RgbColor
from .stage import IStageSink, IStageWriter, LayerEncoder, StageSink, StageWriter
from .vector_graphics_encoder import VectorGraphicsEncoder

__all__ = [
    "END_MARKER_BYTE1",
    "END_MARKER_BYTE2",
    "FrameType",
    "ILayerCanvas",
    "IStageSink",
    "IStageWriter",
    "LayerEncoder",
    "OpType",
    "PropertyId",
    "RgbColor",
    "StageSink",
    "StageWriter",
    "VectorGraphicsEncoder",
]
