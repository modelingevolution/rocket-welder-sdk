#!/usr/bin/env python3
"""DexiNed contour detection example using the RocketWelder SDK.

Runs the BIPED-trained DexiNed edge model (the same "default" model used by
KonturyPoc) on every incoming frame, turns the predicted edge map into closed
contours, and emits each contour as a segmentation instance over the
RocketWelder segmentation sink.

This is the segmentation-writer counterpart of ``05-ball-detector``: instead of
a hand-written threshold detector it uses a neural edge detector, and instead of
a single ball it emits one polygon per detected contour region.

GPU is REQUIRED - the example fails fast if CUDA is not available.

Output is OPEN line segments (the edge centre-lines), not closed contour loops:
the edge map is skeletonized to 1-px centre-lines (cv2.ximgproc.thinning) and
straight segments are pulled out with the probabilistic Hough transform, so a
physical line arrives as a single open 2-point polyline.

Tunable via environment variables:
  MAX_SIDE         longest side fed to the network   (default: 512)
  EDGE_THRESHOLD   sigmoid edge probability cut-off  (default: 0.5)
  HOUGH_THRESHOLD  min votes for a Hough line        (default: 30)
  MIN_LINE_LENGTH  drop segments shorter than (px)   (default: 30)
  MAX_LINE_GAP     bridge gaps up to (px)            (default: 8)
  ORIENTATION      all | horizontal | vertical       (default: all)
  ORIENTATION_TOL  +/- tilt tolerance in degrees     (default: 12)
  MAX_INSTANCES    cap segments emitted per frame    (default: 64)

Required env vars (supplied by the platform):
  CONNECTION_STRING       e.g. shm://buffer?size=10MB&metadata=4KB&mode=Duplex
Optional sinks (NullSink if unset):
  SEGMENTATION_SINK_URL   where contours are streamed
  GRAPHICS_SINK_URL       where the text overlay is streamed
"""

from __future__ import annotations

import logging
import os
import signal
import sys
import threading
import time
from pathlib import Path
from typing import Any, List, Tuple

import cv2
import numpy as np
import numpy.typing as npt
import torch

import rocket_welder_sdk as rw
from rocket_welder_sdk import RgbColor
from rocket_welder_sdk.connection_string import ConnectionMode

# DexiNed network definition lives next to this file (copied from KonturyPoc).
sys.path.insert(0, str(Path(__file__).resolve().parent))
from model import DexiNed

# Single segmentation class - DexiNed is class-agnostic, every contour is "contour".
CONTOUR_CLASS_ID = 1

# DexiNed was trained on BGR images with this per-channel mean subtracted.
MEAN_BGR = np.array([103.939, 116.779, 123.68], dtype=np.float32)

CHECKPOINT = Path(__file__).resolve().parent / "checkpoints" / "BIPED" / "10" / "10_model.pth"


def _env_int(name: str, default: int) -> int:
    try:
        return int(os.environ.get(name, str(default)))
    except ValueError:
        return default


def _env_float(name: str, default: float) -> float:
    try:
        return float(os.environ.get(name, str(default)))
    except ValueError:
        return default


def _round_to_multiple(x: int, m: int = 16) -> int:
    """DexiNed downsamples by 16; the input dims must be a multiple of 16."""
    return ((x + m - 1) // m) * m


class DexiNedContourDetector:
    """Loads DexiNed and converts frames into open line segments (edge centre-lines)."""

    def __init__(self) -> None:
        self._log = logging.getLogger(__name__)

        # Require GPU - fail fast (DexiNed is far too slow on CPU for streaming).
        if not torch.cuda.is_available():
            msg = (
                "CUDA is not available! DexiNed requires GPU acceleration.\n"
                "Ensure the container runs with --gpus all and the NVIDIA "
                "Container Toolkit is installed."
            )
            self._log.error(msg)
            raise RuntimeError(msg)

        if not CHECKPOINT.exists():
            raise FileNotFoundError(f"DexiNed checkpoint not found: {CHECKPOINT}")

        self.device = torch.device("cuda")
        self._log.info("Using device: %s (%s)", self.device, torch.cuda.get_device_name(0))

        self.model = DexiNed().to(self.device)
        state = torch.load(str(CHECKPOINT), map_location=self.device, weights_only=True)
        self.model.load_state_dict(state)
        self.model.eval()
        self._log.info("Loaded DexiNed checkpoint: %s", CHECKPOINT.name)

        # Tunables.
        self.max_side = _env_int("MAX_SIDE", 512)
        self.edge_threshold = _env_float("EDGE_THRESHOLD", 0.5)
        self.max_instances = _env_int("MAX_INSTANCES", 64)
        # Open-line extraction: skeletonize the edge map to 1-px centre-lines, then
        # pull straight OPEN segments out with the probabilistic Hough transform.
        #   HOUGH_THRESHOLD  = min votes for a line            (higher = fewer lines)
        #   MIN_LINE_LENGTH  = drop segments shorter than this (px, network resolution)
        #   MAX_LINE_GAP     = bridge gaps up to this along a line (px)
        self.hough_threshold = _env_int("HOUGH_THRESHOLD", 30)
        self.min_line_length = _env_int("MIN_LINE_LENGTH", 30)
        self.max_line_gap = _env_int("MAX_LINE_GAP", 8)
        # Directional filtering: keep only segments near one orientation.
        #   ORIENTATION      = all | vertical | horizontal   (default: all)
        #   ORIENTATION_TOL  = +/- tilt tolerance in degrees
        self.orientation = os.environ.get("ORIENTATION", "all").strip().lower()
        if self.orientation not in ("all", "vertical", "horizontal"):
            self.orientation = "all"
        self.orientation_tol = _env_int("ORIENTATION_TOL", 12)
        self._log.info(
            "Config: max_side=%d edge_threshold=%.2f max_instances=%d "
            "hough_threshold=%d min_line_length=%d max_line_gap=%d "
            "orientation=%s orientation_tol=%d",
            self.max_side,
            self.edge_threshold,
            self.max_instances,
            self.hough_threshold,
            self.min_line_length,
            self.max_line_gap,
            self.orientation,
            self.orientation_tol,
        )

    @staticmethod
    def _to_bgr(frame: npt.NDArray[Any]) -> npt.NDArray[Any]:
        """Ensure a 3-channel BGR uint8 image regardless of the source format."""
        if frame.ndim == 2:
            return cv2.cvtColor(frame, cv2.COLOR_GRAY2BGR)
        if frame.shape[2] == 1:
            return cv2.cvtColor(frame[:, :, 0], cv2.COLOR_GRAY2BGR)
        return frame

    def _orientation_ok(self, x1: int, y1: int, x2: int, y2: int) -> bool:
        """True if the segment matches the configured ORIENTATION (0=horizontal, 90=vertical)."""
        if self.orientation == "all":
            return True
        ang = abs(float(np.degrees(np.arctan2(abs(y2 - y1), abs(x2 - x1)))))
        tol = max(0, self.orientation_tol)
        if self.orientation == "horizontal":
            return ang <= tol
        return ang >= 90 - tol  # vertical

    @staticmethod
    def _segment_confidence(
        edge: npt.NDArray[np.float32], x1: int, y1: int, x2: int, y2: int
    ) -> float:
        """Mean edge probability sampled along the segment (network resolution)."""
        h, w = edge.shape[:2]
        n = max(2, int(round(float(np.hypot(x2 - x1, y2 - y1)))))
        xs = np.clip(np.linspace(x1, x2, n).astype(np.int32), 0, w - 1)
        ys = np.clip(np.linspace(y1, y2, n).astype(np.int32), 0, h - 1)
        return float(edge[ys, xs].mean())

    def _edge_map(self, bgr: npt.NDArray[Any]) -> npt.NDArray[np.float32]:
        """Return the fused DexiNed edge probability map at network resolution."""
        # Downscale the longest side, then pad up to a multiple of 16.
        h, w = bgr.shape[:2]
        scale = min(1.0, self.max_side / float(max(h, w)))
        net_w = _round_to_multiple(max(16, round(w * scale)))
        net_h = _round_to_multiple(max(16, round(h * scale)))
        resized = cv2.resize(bgr, (net_w, net_h), interpolation=cv2.INTER_LINEAR)

        img = resized.astype(np.float32) - MEAN_BGR
        tensor = torch.from_numpy(img.transpose(2, 0, 1)).unsqueeze(0).to(self.device)

        with torch.no_grad():
            preds = self.model(tensor)
        # preds[-1] is the fused head (head #7), the same one KonturyPoc keeps.
        edge = torch.sigmoid(preds[-1]).squeeze().detach().cpu().numpy()
        return edge.astype(np.float32)

    def detect(self, frame: npt.NDArray[Any]) -> List[Tuple[List[Tuple[int, int]], float]]:
        """Detect OPEN line segments; returns ([(x1,y1),(x2,y2)], confidence) per line.

        Skeletonizes the edge map to 1-px centre-lines and extracts straight OPEN
        segments with HoughLinesP, so a physical line is one open polyline — never a
        closed contour loop (no MORPH_CLOSE, no findContours).
        """
        bgr = self._to_bgr(frame)
        orig_h, orig_w = bgr.shape[:2]

        edge = self._edge_map(bgr)
        net_h, net_w = edge.shape[:2]

        # Binary edge mask (kept thin — NO close), thinned to 1-px centre-lines.
        mask: npt.NDArray[Any] = (edge >= self.edge_threshold).astype(np.uint8) * 255
        skel = cv2.ximgproc.thinning(mask)

        lines = cv2.HoughLinesP(
            skel,
            rho=1,
            theta=np.pi / 180.0,
            threshold=self.hough_threshold,
            minLineLength=self.min_line_length,
            maxLineGap=self.max_line_gap,
        )

        sx = orig_w / float(net_w)
        sy = orig_h / float(net_h)
        results: List[Tuple[List[Tuple[int, int]], float]] = []
        if lines is None:
            return results

        # Longest segments first so MAX_INSTANCES keeps the most prominent lines.
        segs = sorted(
            (ln[0] for ln in lines),
            key=lambda s: (int(s[0]) - int(s[2])) ** 2 + (int(s[1]) - int(s[3])) ** 2,
            reverse=True,
        )

        for x1, y1, x2, y2 in segs:
            if not self._orientation_ok(x1, y1, x2, y2):
                continue
            confidence = self._segment_confidence(edge, x1, y1, x2, y2)
            points = [
                (int(round(x1 * sx)), int(round(y1 * sy))),
                (int(round(x2 * sx)), int(round(y2 * sy))),
            ]
            results.append((points, confidence))
            if len(results) >= self.max_instances:
                break

        return results


class DexiNedService:
    """Wires the detector into the RocketWelder segmentation-writer pipeline."""

    def __init__(self, client: rw.RocketWelderClient, exit_after: int = -1) -> None:
        self._client = client
        self._exit_after = exit_after
        self._frame_count = 0
        self._seg_written = 0
        self._log = logging.getLogger(__name__)
        self._stop_event = threading.Event()
        self._detector = DexiNedContourDetector()

    def run(self, cancellation_token: threading.Event | None = None) -> None:
        self._log.info("Starting DexiNed contour client: %s", self._client.connection)
        self._log.info(
            "Segmentation sink: %s", os.environ.get("SEGMENTATION_SINK_URL") or "(NullSink)"
        )

        if self._client.connection.connection_mode == ConnectionMode.DUPLEX:
            self._log.info("Running in DUPLEX mode (with output preview)")
            self._client.start_with_writers(self._process_frame_duplex, cancellation_token)
        else:
            self._log.info("Running in ONE-WAY mode (sink-only)")
            self._client.start_with_writers_oneway(self._process_frame_oneway, cancellation_token)

        while self._client.is_running and not self._stop_event.is_set():
            if cancellation_token is not None and cancellation_token.is_set():
                break
            time.sleep(0.1)

        self._log.info("Stopping client... Total frames: %d", self._frame_count)
        self._client.stop()

    def _process_frame_oneway(
        self,
        input_frame: npt.NDArray[Any],
        seg_writer: rw.ISegmentationResultWriter,
        kp_writer: rw.IKeyPointsWriter,
        stage_writer: rw.IStageWriter,
    ) -> None:
        self._process_frame_common(input_frame, seg_writer, stage_writer, None)

    def _process_frame_duplex(
        self,
        input_frame: npt.NDArray[Any],
        seg_writer: rw.ISegmentationResultWriter,
        kp_writer: rw.IKeyPointsWriter,
        stage_writer: rw.IStageWriter,
        output_frame: npt.NDArray[Any],
    ) -> None:
        np.copyto(output_frame, input_frame)
        self._process_frame_common(input_frame, seg_writer, stage_writer, output_frame)

    def _process_frame_common(
        self,
        input_frame: npt.NDArray[Any],
        seg_writer: rw.ISegmentationResultWriter,
        stage_writer: rw.IStageWriter,
        output_frame: npt.NDArray[Any] | None,
    ) -> None:
        self._frame_count += 1

        lines = self._detector.detect(input_frame)

        for instance_id, (points, confidence) in enumerate(lines):
            seg_writer.append(CONTOUR_CLASS_ID, instance_id, confidence, points)
            self._seg_written += 1
            if output_frame is not None:
                pts = np.array(points, dtype=np.int32)
                cv2.polylines(output_frame, [pts], False, (0, 255, 0), 1)

        layer = stage_writer[0]
        layer.set_font_size(24)
        layer.set_font_color(RgbColor(255, 255, 255))
        layer.draw_text(f"DexiNed lines: {len(lines)}", 10, 30)

        if self._frame_count % 30 == 0:
            self._log.info(
                "Frame %d: %d lines this frame, %d segmentations written total",
                self._frame_count,
                len(lines),
                self._seg_written,
            )

        if self._exit_after > 0 and self._frame_count >= self._exit_after:
            self._log.info("Reached %d frames, exiting...", self._exit_after)
            self._stop_event.set()


def setup_logging(level: int = logging.INFO) -> None:
    logging.basicConfig(
        level=level,
        format="%(asctime)s.%(msecs)03d [%(levelname)-8s] %(name)s: %(message)s",
        datefmt="%H:%M:%S",
        stream=sys.stdout,
    )


def main() -> None:
    setup_logging()
    logger = logging.getLogger(__name__)

    connection_string = sys.argv[1] if len(sys.argv) > 1 else os.environ.get("CONNECTION_STRING")
    if not connection_string:
        logger.error("No connection string provided (arg or CONNECTION_STRING env)")
        sys.exit(1)

    client = rw.Client(connection_string)
    logger.info("Connected: %s", client.connection)

    service = DexiNedService(client, exit_after=_env_int("EXIT_AFTER", -1))

    stop_event = threading.Event()

    def signal_handler(signum: int, _frame: Any) -> None:
        logger.info("Received signal %d, stopping...", signum)
        stop_event.set()

    signal.signal(signal.SIGINT, signal_handler)
    signal.signal(signal.SIGTERM, signal_handler)

    try:
        service.run(stop_event)
    finally:
        client.stop()
        logger.info("Done. Processed %d frames", service._frame_count)


if __name__ == "__main__":
    main()
