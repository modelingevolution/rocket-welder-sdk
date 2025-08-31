#!/usr/bin/env python3
import os
import subprocess
import time

print("Testing detection of actual root-owned process...")

# Get a real root-owned process PID (systemd or init)
print("\n1. Finding a root-owned process...")
result = subprocess.run(['ps', 'aux'], capture_output=True, text=True)
for line in result.stdout.split('\n')[1:]:  # Skip header
    if line:
        parts = line.split()
        user = parts[0]
        pid = parts[1]
        cmd = ' '.join(parts[10:])
        if user == 'root' and 'systemd' in cmd:
            root_pid = int(pid)
            print(f"   Found root process: PID {root_pid} ({cmd[:50]}...)")
            break
else:
    # Fallback to PID 1
    root_pid = 1
    print(f"   Using PID 1 (init)")

# Try to check if it exists
print(f"\n2. Checking if root PID {root_pid} exists from non-root Python...")
try:
    os.kill(root_pid, 0)
    print(f"   ✓ Process {root_pid} exists (detected successfully)")
except ProcessLookupError:
    print(f"   ✗ Process {root_pid} not found")
except PermissionError as e:
    print(f"   ✗ Permission denied: {e}")
except Exception as e:
    print(f"   ✗ Unexpected error: {e}")

# Test with an invalid PID
print(f"\n3. Testing with invalid PID 99999...")
try:
    os.kill(99999, 0)
    print(f"   ✗ Process 99999 exists (should not happen)")
except ProcessLookupError:
    print(f"   ✓ Process 99999 not found (correct)")
except Exception as e:
    print(f"   ✗ Unexpected error: {e}")