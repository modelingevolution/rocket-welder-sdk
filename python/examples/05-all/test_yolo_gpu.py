#!/usr/bin/env python3
"""
Simple YOLO GPU acceleration test
Tests that YOLO model can run on GPU with video input
"""
import sys
import time

import cv2
import torch
from ultralytics import YOLO


def main():
    print("=" * 60)
    print("YOLO GPU Acceleration Test")
    print("=" * 60)

    # Check CUDA availability
    print(f"\nPyTorch version: {torch.__version__}")
    print(f"CUDA available: {torch.cuda.is_available()}")
    if torch.cuda.is_available():
        print(f"CUDA version: {torch.version.cuda}")
        print(f"GPU device: {torch.cuda.get_device_name(0)}")
        print(f"GPU memory: {torch.cuda.get_device_properties(0).total_memory / 1e9:.2f} GB")
    else:
        print("ERROR: CUDA is not available!")
        sys.exit(1)

    # Check OpenCV
    print(f"\nOpenCV version: {cv2.__version__}")
    print(f"OpenCV location: {cv2.__file__}")

    # Load YOLO model
    print("\n" + "-" * 60)
    print("Loading YOLO model...")
    model = YOLO("yolov8n-seg.pt")  # Nano segmentation model
    model.to("cuda")
    print(f"Model loaded on device: {next(model.model.parameters()).device}")

    # Get video file path from command line or use webcam
    video_source = sys.argv[1] if len(sys.argv) > 1 else 0

    print(f"\nOpening video source: {video_source}")
    cap = cv2.VideoCapture(video_source)

    if not cap.isOpened():
        print(f"ERROR: Cannot open video source: {video_source}")
        sys.exit(1)

    # Get video properties
    fps = cap.get(cv2.CAP_PROP_FPS)
    width = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    height = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
    total_frames = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))

    print("Video properties:")
    print(f"  Resolution: {width}x{height}")
    print(f"  FPS: {fps}")
    print(f"  Total frames: {total_frames}")

    # Process frames
    print("\n" + "-" * 60)
    print("Processing frames (will process 100 frames or until video ends)...")
    print("-" * 60)

    frame_count = 0
    total_inference_time = 0.0
    max_frames = 100

    while frame_count < max_frames:
        ret, frame = cap.read()
        if not ret:
            break

        # Run inference
        start_time = time.time()
        results = model(frame, verbose=False)
        inference_time = time.time() - start_time

        total_inference_time += inference_time
        frame_count += 1

        # Get detection info
        detections = len(results[0].boxes) if results[0].boxes is not None else 0

        # Print progress every 10 frames
        if frame_count % 10 == 0:
            avg_fps = frame_count / total_inference_time
            print(
                f"Frame {frame_count:3d}: {inference_time*1000:6.2f}ms | "
                f"Avg FPS: {avg_fps:5.1f} | Detections: {detections}"
            )

    cap.release()

    # Print summary
    print("\n" + "=" * 60)
    print("Test Summary")
    print("=" * 60)
    print(f"Frames processed: {frame_count}")
    print(f"Total time: {total_inference_time:.2f}s")
    print(f"Average inference time: {total_inference_time/frame_count*1000:.2f}ms")
    print(f"Average FPS: {frame_count/total_inference_time:.1f}")
    print("\n✓ GPU acceleration is working!")
    print("=" * 60)


if __name__ == "__main__":
    main()
