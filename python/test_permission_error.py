#!/usr/bin/env python3
import os

def process_exists_current(pid: int) -> bool:
    """Current implementation in zerobuffer"""
    if pid == 0:
        return False
    try:
        os.kill(pid, 0)
        return True
    except (OSError, ProcessLookupError):
        return False

def process_exists_fixed(pid: int) -> bool:
    """Fixed implementation that handles PermissionError correctly"""
    if pid == 0:
        return False
    try:
        os.kill(pid, 0)
        return True
    except PermissionError:
        # Permission denied means the process exists but we can't signal it
        return True
    except (OSError, ProcessLookupError):
        return False

# Test with root process (PID 1)
print("Testing with PID 1 (root process):")
print(f"  Current implementation: {process_exists_current(1)}")
print(f"  Fixed implementation:   {process_exists_fixed(1)}")

# Test with invalid PID
print("\nTesting with PID 99999 (invalid):")
print(f"  Current implementation: {process_exists_current(99999)}")
print(f"  Fixed implementation:   {process_exists_fixed(99999)}")

# Test with current process
print(f"\nTesting with current PID {os.getpid()}:")
print(f"  Current implementation: {process_exists_current(os.getpid())}")
print(f"  Fixed implementation:   {process_exists_fixed(os.getpid())}")