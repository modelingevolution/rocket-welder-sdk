#!/usr/bin/env python3
import os
import subprocess
import time

print("Testing cross-user process detection...")

# Start a process as root
print("\n1. Starting a process with sudo...")
proc = subprocess.Popen(['sudo', 'sleep', '5'])
root_pid = proc.pid
print(f"   Started sudo process with PID {root_pid}")

time.sleep(0.5)  # Let it start

# Try to check if it exists from non-root Python
print(f"\n2. Checking if PID {root_pid} exists from non-root Python...")
try:
    os.kill(root_pid, 0)
    print(f"   ✓ Process {root_pid} exists (detected successfully)")
except ProcessLookupError:
    print(f"   ✗ Process {root_pid} not found")
except PermissionError as e:
    print(f"   ✗ Permission denied: {e}")
except Exception as e:
    print(f"   ✗ Unexpected error: {e}")

# Clean up
proc.terminate()
proc.wait()
print("\n3. Process terminated")