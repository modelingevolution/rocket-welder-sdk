#!/usr/bin/env python3
import os
import sys

pid = int(sys.argv[1]) if len(sys.argv) > 1 else 1

print(f"Testing PID {pid} from inside container...")
print(f"Current container PID: {os.getpid()}")

try:
    os.kill(pid, 0)
    print(f"✓ Process {pid} exists and is accessible")
except PermissionError as e:
    print(f"✗ Permission denied: {e}")
    print(f"  (Process exists but can't signal it)")
except ProcessLookupError as e:
    print(f"✗ Process not found: {e}")
except Exception as e:
    print(f"✗ Unexpected error: {type(e).__name__}: {e}")

# List processes visible in container
print("\nProcesses visible in container (first 10):")
os.system("ps aux | head -11")