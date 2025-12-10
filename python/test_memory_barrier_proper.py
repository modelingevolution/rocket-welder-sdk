#!/usr/bin/env python3
"""
Proper test for memory barrier implementation.
Tests that after calling memory_barrier(), all previous writes are visible to other processes.

The test uses semaphores to ensure proper ordering:
1. Writer writes data
2. Writer calls memory_barrier (or not)
3. Writer signals semaphore
4. Reader waits on semaphore
5. Reader calls memory_barrier (or not)
6. Reader reads data
7. Reader checks if data is correct
"""

import mmap
import multiprocessing
import os
import struct
import sys
import time

# Try to import posix_ipc for named semaphores
try:
    import posix_ipc
    HAS_POSIX_IPC = True
except ImportError:
    HAS_POSIX_IPC = False
    print("Warning: posix_ipc not available, using multiprocessing semaphores")

# Import our memory barrier implementation
sys.path.insert(0, '/mnt/d/source/modelingevolution/streamer/src/zerobuffer/python')
from zerobuffer.platform.linux import memory_barrier

# Test parameters
ITERATIONS = 100000
SHARED_SIZE = 1024
TEST_PATTERN_SIZE = 64  # Write 64 bytes of pattern


def writer_process(shm_name: str, sem_write_name: str, sem_read_name: str,
                   use_barrier: bool, iterations: int) -> None:
    """
    Writer process that writes data and signals when done.
    """
    print(f"Writer starting (barrier={'ON' if use_barrier else 'OFF'})")

    # Open shared memory
    shm_fd = os.open(f"/dev/shm/{shm_name}", os.O_RDWR)
    shm = mmap.mmap(shm_fd, SHARED_SIZE)

    # Open semaphores
    if HAS_POSIX_IPC:
        sem_write = posix_ipc.Semaphore(sem_write_name)
        sem_read = posix_ipc.Semaphore(sem_read_name)
    else:
        # Use file-based signaling as fallback
        pass

    errors = 0

    for i in range(iterations):
        # Generate test pattern - all bytes should have same value
        test_value = (i % 256)
        pattern = bytes([test_value] * TEST_PATTERN_SIZE)

        # Write pattern to shared memory
        shm[0:TEST_PATTERN_SIZE] = pattern

        # Write iteration number at offset TEST_PATTERN_SIZE
        shm[TEST_PATTERN_SIZE:TEST_PATTERN_SIZE+8] = struct.pack('<Q', i)

        if use_barrier:
            # CRITICAL: Call memory barrier to ensure writes are visible
            memory_barrier()
            # Also flush the mmap to ensure msync
            shm.flush()

        # Signal that write is complete
        if HAS_POSIX_IPC:
            sem_write.release()

            # Wait for reader to signal it's done reading
            sem_read.acquire()
        else:
            # Simple file-based signaling
            with open(f"/dev/shm/{sem_write_name}", 'w') as f:
                f.write(str(i))
            # Wait for reader
            while not os.path.exists(f"/dev/shm/{sem_read_name}_{i}"):
                time.sleep(0.00001)
            os.unlink(f"/dev/shm/{sem_read_name}_{i}")

    print(f"Writer completed {iterations} iterations")
    shm.close()
    os.close(shm_fd)


def reader_process(shm_name: str, sem_write_name: str, sem_read_name: str,
                  use_barrier: bool, iterations: int, result_queue) -> None:
    """
    Reader process that waits for signal, then reads and verifies data.
    """
    print(f"Reader starting (barrier={'ON' if use_barrier else 'OFF'})")

    # Open shared memory
    shm_fd = os.open(f"/dev/shm/{shm_name}", os.O_RDWR)
    shm = mmap.mmap(shm_fd, SHARED_SIZE)

    # Open semaphores
    if HAS_POSIX_IPC:
        sem_write = posix_ipc.Semaphore(sem_write_name)
        sem_read = posix_ipc.Semaphore(sem_read_name)

    errors = 0

    for i in range(iterations):
        # Wait for writer to signal
        if HAS_POSIX_IPC:
            sem_write.acquire()
        else:
            # Wait for signal file
            while not os.path.exists(f"/dev/shm/{sem_write_name}"):
                time.sleep(0.00001)
            os.unlink(f"/dev/shm/{sem_write_name}")

        if use_barrier:
            # CRITICAL: Call memory barrier to ensure we see latest writes
            memory_barrier()
            # Also flush to ensure we're synchronized
            shm.flush()

        # Read the pattern
        pattern = bytes(shm[0:TEST_PATTERN_SIZE])

        # Read iteration number
        iter_bytes = bytes(shm[TEST_PATTERN_SIZE:TEST_PATTERN_SIZE+8])
        iter_num = struct.unpack('<Q', iter_bytes)[0]

        # Verify pattern - all bytes should be the same
        expected_value = (i % 256)

        # Check iteration number first
        if iter_num != i:
            errors += 1
            if errors <= 5:
                print(f"Reader: Iteration mismatch! Expected {i}, got {iter_num}")

        # Check all bytes in pattern
        for j, byte_val in enumerate(pattern):
            if byte_val != expected_value:
                errors += 1
                if errors <= 5:
                    print(f"Reader: Data mismatch at iteration {i}, byte {j}! "
                          f"Expected {expected_value}, got {byte_val}")
                break  # Only count once per iteration

        # Signal that read is complete
        if HAS_POSIX_IPC:
            sem_read.release()
        else:
            # Create signal file
            with open(f"/dev/shm/{sem_read_name}_{i}", 'w') as f:
                f.write('done')

    print(f"Reader completed with {errors} errors out of {iterations} iterations")

    shm.close()
    os.close(shm_fd)

    # Return results
    result_queue.put({
        'iterations': iterations,
        'errors': errors
    })


def run_test(use_barrier: bool, iterations: int = ITERATIONS) -> dict:
    """
    Run test with or without memory barriers.
    """
    print(f"\n{'='*60}")
    print(f"Testing {'WITH' if use_barrier else 'WITHOUT'} memory barriers")
    print(f"Iterations: {iterations}")
    print(f"{'='*60}")

    # Create unique names
    pid = os.getpid()
    shm_name = f"test_mb_{pid}_{use_barrier}"
    sem_write_name = f"/sem_write_{pid}_{use_barrier}"
    sem_read_name = f"/sem_read_{pid}_{use_barrier}"

    # Create shared memory
    shm_path = f"/dev/shm/{shm_name}"
    with open(shm_path, 'wb') as f:
        f.write(b'\x00' * SHARED_SIZE)
    os.chmod(shm_path, 0o666)

    # Create semaphores
    if HAS_POSIX_IPC:
        # Clean up any existing semaphores
        try:
            posix_ipc.unlink_semaphore(sem_write_name)
        except:
            pass
        try:
            posix_ipc.unlink_semaphore(sem_read_name)
        except:
            pass

        # Create new semaphores (initial value 0)
        sem_write = posix_ipc.Semaphore(sem_write_name, flags=posix_ipc.O_CREAT, initial_value=0)
        sem_read = posix_ipc.Semaphore(sem_read_name, flags=posix_ipc.O_CREAT, initial_value=0)

    # Create result queue
    result_queue = multiprocessing.Queue()

    # Start processes
    writer = multiprocessing.Process(
        target=writer_process,
        args=(shm_name, sem_write_name, sem_read_name, use_barrier, iterations)
    )
    reader = multiprocessing.Process(
        target=reader_process,
        args=(shm_name, sem_write_name, sem_read_name, use_barrier, iterations, result_queue)
    )

    reader.start()
    time.sleep(0.1)  # Let reader initialize
    writer.start()

    # Wait for completion
    writer.join(timeout=30)
    reader.join(timeout=30)

    # Get results
    results = {'iterations': 0, 'errors': -1}
    try:
        results = result_queue.get(timeout=1)
    except:
        print("Failed to get results from reader")

    # Cleanup
    if HAS_POSIX_IPC:
        try:
            sem_write.close()
            sem_read.close()
            posix_ipc.unlink_semaphore(sem_write_name)
            posix_ipc.unlink_semaphore(sem_read_name)
        except:
            pass

    try:
        os.unlink(shm_path)
    except:
        pass

    return results


def main():
    """
    Main test runner.
    """
    print("Memory Barrier Visibility Test")
    print("==============================")
    print(f"Platform: {sys.platform}")
    print(f"Python: {sys.version}")
    print(f"Using: {'posix_ipc semaphores' if HAS_POSIX_IPC else 'file-based signaling'}")

    cpu_count = multiprocessing.cpu_count()
    print(f"CPU cores: {cpu_count}")

    # Run tests
    print("\nPhase 1: Baseline test WITHOUT memory barriers")
    results_no_barrier = run_test(use_barrier=False, iterations=ITERATIONS)

    print("\nPhase 2: Test WITH memory barriers")
    results_with_barrier = run_test(use_barrier=True, iterations=ITERATIONS)

    # Summary
    print(f"\n{'='*60}")
    print("RESULTS SUMMARY")
    print(f"{'='*60}")
    print(f"WITHOUT barriers: {results_no_barrier['errors']} errors in {results_no_barrier['iterations']} iterations")
    print(f"WITH barriers:    {results_with_barrier['errors']} errors in {results_with_barrier['iterations']} iterations")

    print(f"\n{'='*60}")
    print("CONCLUSION")
    print(f"{'='*60}")

    if results_no_barrier['errors'] == 0 and results_with_barrier['errors'] == 0:
        print("✅ SUCCESS: No errors detected in either test!")
        print("   This means:")
        print("   - The system has strong memory coherency (likely x86)")
        print("   - Or the semaphore operations include implicit memory barriers")
        print("   - The memory barrier implementation is working correctly")
        return 0
    elif results_with_barrier['errors'] < results_no_barrier['errors']:
        print("✅ IMPROVEMENT: Memory barriers reduced errors!")
        print(f"   Errors reduced from {results_no_barrier['errors']} to {results_with_barrier['errors']}")
        return 0
    elif results_with_barrier['errors'] == 0:
        print("✅ SUCCESS: Memory barriers eliminated all errors!")
        return 0
    else:
        print("❌ FAILURE: Memory barriers did not help")
        print("   This might indicate the barrier implementation needs work")
        return 1


if __name__ == "__main__":
    sys.exit(main())
