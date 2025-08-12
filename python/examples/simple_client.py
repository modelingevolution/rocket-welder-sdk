#!/usr/bin/env python3
"""
Simple example client for RocketWelder SDK.
"""

import sys
import signal
import cv2
import numpy as np
import rocket_welder_sdk as rw
from datetime import datetime

# Global flag for graceful shutdown
running = True

def signal_handler(sig, frame):
    global running
    print("\nStopping client...")
    running = False

def main():
    global running
    
    # Set up signal handler
    signal.signal(signal.SIGINT, signal_handler)
    
    print("Starting RocketWelder client...")
    
    # Parse exit-after parameter
    exit_after = -1  # -1 means run forever
    for arg in sys.argv[1:]:
        if arg.startswith("--exit-after="):
            exit_after = int(arg[13:])
            break
    
    # Create client from command line args and environment
    client = rw.Client.from_args(sys.argv)
    
    frame_count = 0
    
    # Set up frame processing callback
    @client.on_frame
    def process_frame(frame: np.ndarray):
        nonlocal frame_count
        global running
        
        frame_count += 1
        
        # Add overlay text
        cv2.putText(frame, "Processing", (10, 30),
                    cv2.FONT_HERSHEY_SIMPLEX, 1.0, (0, 255, 0), 2)
        
        # Add timestamp overlay
        timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        cv2.putText(frame, timestamp, (10, 60),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
        
        # Add frame counter
        cv2.putText(frame, f"Frame: {frame_count}", (10, 90),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
        
        print(f"Processed frame {frame_count} ({frame.shape[1]}x{frame.shape[0]})")
        
        # Check if we should exit
        if exit_after > 0 and frame_count >= exit_after:
            print(f"Reached {exit_after} frames, exiting...")
            running = False
    
    # Start processing
    if exit_after > 0:
        print(f"Will exit after {exit_after} frames")
    client.start()
    
    # Run until interrupted or frame limit reached
    import time
    while running and client.is_running():
        time.sleep(0.1)
    
    # Stop processing
    print("Stopping client...")
    print(f"Total frames processed: {frame_count}")
    client.stop()

if __name__ == "__main__":
    main()