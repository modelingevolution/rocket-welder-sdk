#!/usr/bin/env python3
"""
YOLO segmentation stress generator for native-player overlay performance testing.

SINK-ONLY: emits dense, high-vertex segmentation polygons over the
RocketWelder segmentation sink (zerobuffer/unix-socket). Frames come from a
GStreamer pipeline (filesrc -> zerosink) outside this process.

Stress knobs (env vars):
  YOLO_MODEL          ultralytics model name        (default: yolov8x-seg.pt)
  CONF_THRESHOLD      detection confidence floor    (default: 0.05)
  INSTANCE_MULTIPLIER re-emit each polygon N times  (default: 1)
  CONTOUR_MODE        "none" -> CHAIN_APPROX_NONE (raw, dense vertices)
                      "simple" -> CHAIN_APPROX_SIMPLE (default)

Required env vars:
  CONNECTION_STRING       e.g. shm://rw-stress?size=64MB&metadata=4KB
  SEGMENTATION_SINK_URL   sink consumed by native-player overlay
"""

from __future__ import annotations

import logging
import os
import signal
import sys
import threading
import time
from typing import Any, List, Tuple

import cv2
import numpy as np
import numpy.typing as npt
import torch
from ultralytics import YOLO

import rocket_welder_sdk as rw
from rocket_welder_sdk import RgbColor


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


class YoloStressService:
    def __init__(self, client: rw.RocketWelderClient) -> None:
        self._client = client
        self._log = logging.getLogger(__name__)
        self._stop = threading.Event()

        if not torch.cuda.is_available():
            raise RuntimeError(
                "CUDA required. Run with --runtime=nvidia --gpus all."
            )

        self._model_name = os.environ.get("YOLO_MODEL", "yolov8x-seg.pt")
        self._conf = _env_float("CONF_THRESHOLD", 0.05)
        self._multiplier = max(1, _env_int("INSTANCE_MULTIPLIER", 1))
        self._contour_mode = (
            cv2.CHAIN_APPROX_NONE
            if os.environ.get("CONTOUR_MODE", "none").lower() == "none"
            else cv2.CHAIN_APPROX_SIMPLE
        )

        self._log.info(
            "Loading %s on %s (conf=%.2f, mult=%d)",
            self._model_name,
            torch.cuda.get_device_name(0),
            self._conf,
            self._multiplier,
        )
        self._model = YOLO(self._model_name)
        self._model.to("cuda")

        self._frames = 0
        self._instances = 0
        self._vertices = 0
        self._t0 = time.time()

    def run(self) -> None:
        from rocket_welder_sdk.connection_string import ConnectionMode

        if self._client.connection.connection_mode == ConnectionMode.DUPLEX:
            self._log.info("DUPLEX mode")
            self._client.start_with_writers(self._on_frame_duplex)
        else:
            self._log.info("ONE-WAY mode")
            self._client.start_with_writers_oneway(self._on_frame_oneway)

        while self._client.is_running and not self._stop.is_set():
            time.sleep(0.1)
        self._client.stop()

    def stop(self) -> None:
        self._stop.set()

    def _on_frame_oneway(
        self,
        frame: npt.NDArray[Any],
        seg: rw.ISegmentationResultWriter,
        kp: rw.IKeyPointsWriter,
        stage: rw.IStageWriter,
    ) -> None:
        self._process(frame, seg, stage)

    def _on_frame_duplex(
        self,
        frame: npt.NDArray[Any],
        seg: rw.ISegmentationResultWriter,
        kp: rw.IKeyPointsWriter,
        stage: rw.IStageWriter,
        output: npt.NDArray[Any],
    ) -> None:
        self._process(frame, seg, stage)

    def _extract_polygons(
        self, mask: npt.NDArray[Any], h: int, w: int
    ) -> List[List[Tuple[int, int]]]:
        m = cv2.resize(mask, (w, h), interpolation=cv2.INTER_LINEAR)
        binary = (m > 0.5).astype(np.uint8) * 255
        contours, _ = cv2.findContours(
            binary, cv2.RETR_EXTERNAL, self._contour_mode
        )
        polys: List[List[Tuple[int, int]]] = []
        for c in contours:
            if len(c) < 3:
                continue
            polys.append([(int(p[0][0]), int(p[0][1])) for p in c])
        return polys

    def _process(
        self,
        frame: npt.NDArray[Any],
        seg: rw.ISegmentationResultWriter,
        stage: rw.IStageWriter,
    ) -> None:
        self._frames += 1
        h, w = frame.shape[:2]

        if frame.ndim == 2:
            rgb = cv2.cvtColor(frame, cv2.COLOR_GRAY2RGB)
        elif frame.shape[2] == 1:
            rgb = cv2.cvtColor(frame[:, :, 0], cv2.COLOR_GRAY2RGB)
        else:
            rgb = frame

        results = self._model(rgb, conf=self._conf, verbose=False)
        if not results or results[0].masks is None:
            self._draw_stats(stage)
            return

        r = results[0]
        masks = r.masks.data.cpu().numpy()
        classes = r.boxes.cls.cpu().numpy().astype(int)
        confs = r.boxes.conf.cpu().numpy()

        instance_id = 0
        frame_vertices = 0
        for mask, cls, conf in zip(masks, classes, confs):
            for poly in self._extract_polygons(mask, h, w):
                for k in range(self._multiplier):
                    if k == 0:
                        pts = poly
                    else:
                        dx = (k * 7) % 13 - 6
                        dy = (k * 11) % 17 - 8
                        pts = [(x + dx, y + dy) for (x, y) in poly]
                    seg.append(int(cls), instance_id, float(conf), pts)
                    instance_id += 1
                    frame_vertices += len(pts)

        self._instances += instance_id
        self._vertices += frame_vertices
        self._draw_stats(stage, instance_id, frame_vertices)

    def _draw_stats(
        self,
        stage: rw.IStageWriter,
        instances: int = 0,
        vertices: int = 0,
    ) -> None:
        layer = stage[0]
        layer.set_font_size(20)
        layer.set_font_color(RgbColor(255, 255, 255))
        elapsed = max(1e-3, time.time() - self._t0)
        fps = self._frames / elapsed
        layer.draw_text(
            f"frame={self._frames} fps={fps:.1f} "
            f"inst/frame={instances} verts/frame={vertices} "
            f"total_inst={self._instances} total_verts={self._vertices}",
            10,
            30,
        )


def main() -> None:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
        stream=sys.stdout,
    )
    log = logging.getLogger(__name__)

    conn = os.environ.get("CONNECTION_STRING") or (
        sys.argv[1] if len(sys.argv) > 1 else None
    )
    if not conn:
        log.error("CONNECTION_STRING not set")
        sys.exit(1)

    client = rw.Client(conn)
    service = YoloStressService(client)

    def _signal(signum: int, _frame: Any) -> None:
        log.info("signal %d, stopping", signum)
        service.stop()

    signal.signal(signal.SIGINT, _signal)
    signal.signal(signal.SIGTERM, _signal)

    service.run()


if __name__ == "__main__":
    main()
