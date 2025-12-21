#!/usr/bin/env python3
"""
YOLO Segmentation example using RocketWelder SDK.
Performs real-time instance segmentation on video frames using YOLOv8.
"""

from __future__ import annotations  # Enable Python 3.9+ type hints in Python 3.8

import logging
import sys
import time
from typing import Any, Callable, Optional, Union

import cv2
import numpy as np
import numpy.typing as npt
import torch
from ultralytics import YOLO

import rocket_welder_sdk as rw


def setup_logging() -> logging.Logger:
    """Setup logging with console and file handlers."""
    # Create main logger
    logger = logging.getLogger(__name__)
    logger.setLevel(logging.DEBUG)

    # Clear any existing handlers
    logger.handlers.clear()

    # Create formatters
    simple_formatter = logging.Formatter(
        "%(asctime)s - %(name)s - %(levelname)s - %(message)s", datefmt="%Y-%m-%d %H:%M:%S"
    )

    # Console handler
    console_handler = logging.StreamHandler(sys.stdout)
    console_handler.setLevel(logging.INFO)
    console_handler.setFormatter(simple_formatter)
    logger.addHandler(console_handler)

    # File handler
    file_handler = logging.FileHandler("/tmp/yolo_client.log")
    file_handler.setLevel(logging.DEBUG)
    file_handler.setFormatter(simple_formatter)
    logger.addHandler(file_handler)

    # Configure rocket-welder-sdk logging
    rw_logger = logging.getLogger("rocket_welder_sdk")
    rw_logger.setLevel(logging.INFO)
    rw_logger.handlers.clear()
    rw_logger.addHandler(console_handler)
    rw_logger.addHandler(file_handler)
    rw_logger.propagate = False

    return logger


# Global logger instance
logger: Optional[logging.Logger] = None


def log(message: str, level: int = logging.INFO) -> None:
    """Log a message to both console and file."""
    if logger:
        logger.log(level, message)


class YOLOSegmentationProcessor:
    """Processes frames with YOLO segmentation model."""

    def __init__(self, width: int = 1024, height: int = 1024) -> None:
        """Initialize YOLO model.

        Args:
            width: Expected frame width (default: 1024)
            height: Expected frame height (default: 1024)

        Raises:
            RuntimeError: If CUDA is not available
        """
        # Require GPU - fail fast if not available
        if not torch.cuda.is_available():
            error_msg = (
                "CUDA is not available! This application requires GPU acceleration.\n"
                "Please ensure:\n"
                "  1. NVIDIA GPU is present\n"
                "  2. NVIDIA drivers are installed\n"
                "  3. Docker is running with --runtime=nvidia --gpus all\n"
                "  4. NVIDIA Container Toolkit is installed"
            )
            log(error_msg, logging.ERROR)
            raise RuntimeError(error_msg)

        # GPU is available - log details
        self.device = "cuda"
        log(f"Using device: {self.device}")
        log(f"GPU: {torch.cuda.get_device_name(0)}")
        log(f"CUDA version: {torch.version.cuda}")

        log("Loading YOLO segmentation model...")
        # Load YOLOv8 segmentation model (yolov8n-seg is the nano version)
        self.model = YOLO("yolov8n-seg.pt")
        # Move model to GPU
        self.model.to(self.device)
        log(f"YOLO model loaded successfully on {self.device}")

        # Color map for different classes
        self.colors = self._generate_colors(80)  # COCO has 80 classes

        # Stats
        self.frame_count = 0
        self.total_inference_time = 0.0

        # Frame dimensions
        self.width = width
        self.height = height
        log(f"Expected frame size: {width}x{height}")

    def _generate_colors(self, num_classes: int) -> list[tuple[int, int, int]]:
        """Generate distinct colors for each class."""
        np.random.seed(42)
        colors = []
        for _ in range(num_classes):
            colors.append(
                (
                    int(np.random.randint(0, 255)),
                    int(np.random.randint(0, 255)),
                    int(np.random.randint(0, 255)),
                )
            )
        return colors

    def process_frame(self, frame: npt.NDArray[Any]) -> None:
        """Process frame with YOLO segmentation (in-place modification)."""
        start_time = time.time()

        # Log actual frame dimensions on first frame
        if self.frame_count == 0:
            log(
                f"Received first frame: shape={frame.shape}, dtype={frame.dtype}",
                logging.INFO,
            )

        # Convert grayscale to RGB if needed (YOLO expects 3 channels)
        if len(frame.shape) == 2 or (len(frame.shape) == 3 and frame.shape[2] == 1):
            # Grayscale image - convert to RGB
            frame_gray = frame[:, :, 0] if len(frame.shape) == 3 else frame
            frame_rgb = cv2.cvtColor(frame_gray, cv2.COLOR_GRAY2RGB)
        else:
            # Already RGB
            frame_rgb = frame

        # Run YOLO inference on RGB frame
        results = self.model(frame_rgb, verbose=False)

        # Process results
        if results and len(results) > 0:
            result = results[0]

            # Draw segmentation masks and labels
            if result.masks is not None:
                masks = result.masks.data.cpu().numpy()
                boxes = result.boxes.xyxy.cpu().numpy()
                classes = result.boxes.cls.cpu().numpy().astype(int)
                confidences = result.boxes.conf.cpu().numpy()

                # Create overlay for masks (work with RGB frame)
                overlay = frame_rgb.copy()

                for mask, box, cls, conf in zip(masks, boxes, classes, confidences):
                    # Resize mask to frame size
                    mask_resized = cv2.resize(
                        mask,
                        (frame_rgb.shape[1], frame_rgb.shape[0]),
                        interpolation=cv2.INTER_LINEAR,
                    )

                    # Apply color mask
                    color = self.colors[cls % len(self.colors)]
                    colored_mask = np.zeros_like(frame_rgb)
                    colored_mask[mask_resized > 0.5] = color

                    # Blend with overlay
                    overlay = cv2.addWeighted(overlay, 1.0, colored_mask, 0.4, 0)

                    # Draw bounding box
                    x1, y1, x2, y2 = map(int, box)
                    cv2.rectangle(overlay, (x1, y1), (x2, y2), color, 2)

                    # Draw label with confidence
                    label = f"{result.names[cls]}: {conf:.2f}"
                    label_size, _ = cv2.getTextSize(label, cv2.FONT_HERSHEY_SIMPLEX, 0.5, 1)
                    cv2.rectangle(
                        overlay,
                        (x1, y1 - label_size[1] - 10),
                        (x1 + label_size[0], y1),
                        color,
                        -1,
                    )
                    cv2.putText(
                        overlay,
                        label,
                        (x1, y1 - 5),
                        cv2.FONT_HERSHEY_SIMPLEX,
                        0.5,
                        (255, 255, 255),
                        1,
                    )

                # Update frame_rgb with overlay
                frame_rgb = overlay

        # Update stats
        inference_time = time.time() - start_time
        self.frame_count += 1
        self.total_inference_time += inference_time

        # Add stats overlay
        fps = 1.0 / inference_time if inference_time > 0 else 0
        avg_fps = (
            self.frame_count / self.total_inference_time if self.total_inference_time > 0 else 0
        )

        stats_text = f"FPS: {fps:.1f} | Avg: {avg_fps:.1f} | Frames: {self.frame_count}"
        cv2.putText(frame_rgb, stats_text, (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2)

        # Copy RGB result back to original frame
        # If input was grayscale, convert back to grayscale
        if len(frame.shape) == 2 or (len(frame.shape) == 3 and frame.shape[2] == 1):
            # Convert RGB result back to grayscale for output
            frame_out_gray = cv2.cvtColor(frame_rgb, cv2.COLOR_RGB2GRAY)
            if len(frame.shape) == 3:
                np.copyto(frame[:, :, 0], frame_out_gray)
            else:
                np.copyto(frame, frame_out_gray)
        else:
            # Copy RGB to RGB
            np.copyto(frame, frame_rgb)

    def process_frame_duplex(
        self, input_frame: npt.NDArray[Any], output_frame: npt.NDArray[Any]
    ) -> None:
        """Process frame in duplex mode (copy input to output then process)."""
        np.copyto(output_frame, input_frame)
        self.process_frame(output_frame)


def main() -> None:
    """Main entry point."""
    # Initialize logging
    global logger
    logger = setup_logging()

    log("Starting YOLO Segmentation Client")

    # Create client - automatically detects connection from args or env
    client = rw.Client.from_(sys.argv)
    log(f"Connected: {client.connection}")

    # Initialize YOLO processor with default dimensions
    # Actual dimensions will be detected from first frame
    processor = YOLOSegmentationProcessor(width=1024, height=1024)

    # Select callback based on connection mode
    callback: Union[
        Callable[[npt.NDArray[Any]], None],
        Callable[[npt.NDArray[Any], npt.NDArray[Any]], None],
    ]

    if client.connection.connection_mode == rw.ConnectionMode.DUPLEX:
        log("Using DUPLEX mode")
        callback = processor.process_frame_duplex
    else:
        log("Using ONE-WAY mode")
        callback = processor.process_frame

    # Start processing
    log("Starting frame processing...")
    client.start(callback)

    # Check if preview is enabled
    try:
        if client.connection.parameters.get("preview", "false").lower() == "true":
            log("Showing preview... Press 'q' to stop")
            client.show()
        else:
            # No preview, just keep running
            log("Running without preview... Press Ctrl+C to stop")
            while client.is_running:
                time.sleep(0.1)
    except KeyboardInterrupt:
        log("Stopping...")
    finally:
        client.stop()
        log(f"Processed {processor.frame_count} frames")
        if processor.total_inference_time > 0:
            avg_fps = processor.frame_count / processor.total_inference_time
            log(f"Average FPS: {avg_fps:.2f}")


if __name__ == "__main__":
    main()
