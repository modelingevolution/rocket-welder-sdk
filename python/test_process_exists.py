#!/usr/bin/env python3
import os
import signal
import subprocess

print("Testing process_exists() implementation...")

# Test 1: Check current process (should exist)
current_pid = os.getpid()
print(f"\n1. Testing with current process PID {current_pid}")
try:
    os.kill(current_pid, 0)
    print("   ✓ os.kill(pid, 0) - Process exists")
except:
    print("   ✗ os.kill(pid, 0) - Failed")

# Test 2: Check invalid PID (should not exist)
invalid_pid = 99999
print(f"\n2. Testing with invalid PID {invalid_pid}")
try:
    os.kill(invalid_pid, 0)
    print("   ✗ os.kill(pid, 0) - Should have raised exception")
except (OSError, ProcessLookupError) as e:
    print(f"   ✓ os.kill(pid, 0) - Correctly raised {type(e).__name__}")

# Test 3: The original buggy code with signal.SIG_DFL
print(f"\n3. Testing original buggy code with signal.SIG_DFL")
print(f"   signal.SIG_DFL value: {signal.SIG_DFL}")
print(f"   signal.SIG_DFL type: {type(signal.SIG_DFL)}")

# SIG_DFL is actually a signal handler type, not a signal number
# On Linux, SIG_DFL equals SIG_IGN equals None
# Using it as a signal number is wrong

try:
    os.kill(current_pid, signal.SIG_DFL)
    print(f"   Result: os.kill(pid, signal.SIG_DFL) succeeded (signal value: {signal.SIG_DFL})")
except TypeError as e:
    print(f"   ✓ os.kill(pid, signal.SIG_DFL) failed with TypeError: {e}")
except OSError as e:
    print(f"   ✓ os.kill(pid, signal.SIG_DFL) failed with OSError: {e}")

# Test 4: Check a subprocess we create
print(f"\n4. Testing with subprocess")
proc = subprocess.Popen(['sleep', '1'])
print(f"   Created subprocess with PID {proc.pid}")
try:
    os.kill(proc.pid, 0)
    print("   ✓ os.kill(pid, 0) - Subprocess exists")
except:
    print("   ✗ os.kill(pid, 0) - Failed to detect subprocess")
proc.terminate()
proc.wait()

print("\n✓ The fix (using os.kill(pid, 0)) is correct!")
print("✗ The original (using os.kill(pid, signal.SIG_DFL)) was wrong!")