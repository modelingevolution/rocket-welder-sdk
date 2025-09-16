#!/usr/bin/env python3
"""
Example showing how to display video preview with RocketWelder SDK.

IMPORTANT: On X11/WSL systems, the ?preview=true parameter may cause blocking
due to OpenCV threading limitations. This example shows the recommended approach
of handling preview in the main thread.
"""

import sys
import time
import threading
import cv2
import numpy as np
import rocket_welder_sdk as rw


def main_thread_preview():
    """
    Recommended approach: Handle preview in main thread.
    This works reliably on all systems including X11/WSL.
    """
    print("=== Main Thread Preview (Recommended) ===")
    print("This approach handles display in the main thread")
    print("Press 'q' in preview window to quit\n")

    # No preview parameter - we'll handle display ourselves
    connection_string = "file:///mnt/d/tmp/stream.mp4"
    client = rw.Client(connection_string)

    frame_count = 0
    latest_frame = None
    frame_lock = threading.Lock()
    stop_event = threading.Event()

    def process_frame(frame):
        nonlocal frame_count, latest_frame
        frame_count += 1

        # Store latest frame for main thread to display
        with frame_lock:
            latest_frame = frame.copy()

        # Add any processing here (e.g., overlay text)
        if frame_count % 25 == 0:  # Log every second at 25fps
            print(f"Processed {frame_count} frames")

    # Create preview window in main thread
    window_name = 'RocketWelder Preview (Main Thread)'
    cv2.namedWindow(window_name, cv2.WINDOW_NORMAL)
    cv2.resizeWindow(window_name, 640, 480)

    try:
        client.start(process_frame)

        # Main thread handles display
        while not stop_event.is_set() and frame_count < 125:  # 5 seconds max
            with frame_lock:
                if latest_frame is not None:
                    # Add frame counter overlay
                    display_frame = latest_frame.copy()
                    cv2.putText(display_frame, f"Frame: {frame_count}",
                               (10, 30), cv2.FONT_HERSHEY_SIMPLEX,
                               0.7, (0, 255, 0), 2)

                    cv2.imshow(window_name, display_frame)

            # Process window events
            key = cv2.waitKey(1) & 0xFF
            if key == ord('q'):
                print("User pressed 'q', stopping...")
                stop_event.set()

    finally:
        client.stop()
        cv2.destroyAllWindows()
        print(f"Total frames processed: {frame_count}\n")


def duplex_mode_preview():
    """
    Example with duplex mode showing processed output in preview.
    """
    print("=== Duplex Mode with Main Thread Preview ===")
    print("Shows the OUTPUT frame after processing")
    print("Press 'q' in preview window to quit\n")

    # Duplex mode without preview parameter
    connection_string = "file:///mnt/d/tmp/stream.mp4?mode=Duplex"
    client = rw.Client(connection_string)

    frame_count = 0
    latest_output = None
    frame_lock = threading.Lock()
    stop_event = threading.Event()

    def process_frame_duplex(input_frame, output_frame):
        nonlocal frame_count, latest_output
        frame_count += 1

        # Process: Add edge detection as example
        gray = cv2.cvtColor(input_frame, cv2.COLOR_BGR2GRAY)
        edges = cv2.Canny(gray, 50, 150)
        output_frame[:] = cv2.cvtColor(edges, cv2.COLOR_GRAY2BGR)

        # Add timestamp overlay
        timestamp = time.strftime("%H:%M:%S")
        cv2.putText(output_frame, f"Processed: {timestamp}",
                   (10, 30), cv2.FONT_HERSHEY_SIMPLEX,
                   0.7, (0, 255, 0), 2)

        # Store for main thread display
        with frame_lock:
            latest_output = output_frame.copy()

        if frame_count % 25 == 0:
            print(f"Processed {frame_count} frames")

    # Create preview window
    window_name = 'RocketWelder Preview (Duplex Output)'
    cv2.namedWindow(window_name, cv2.WINDOW_NORMAL)
    cv2.resizeWindow(window_name, 640, 480)

    try:
        client.start(process_frame_duplex)

        # Display loop
        while not stop_event.is_set() and frame_count < 125:  # 5 seconds max
            with frame_lock:
                if latest_output is not None:
                    cv2.imshow(window_name, latest_output)

            key = cv2.waitKey(1) & 0xFF
            if key == ord('q'):
                print("User pressed 'q', stopping...")
                stop_event.set()

    finally:
        client.stop()
        cv2.destroyAllWindows()
        print(f"Total frames processed: {frame_count}\n")


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "duplex":
        duplex_mode_preview()
    else:
        main_thread_preview()

    print("To test duplex mode, run: python preview_example.py duplex")