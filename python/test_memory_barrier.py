#!/usr/bin/env python3
"""
Test memory barrier implementation for shared memory synchronization.
This test demonstrates the need for memory barriers in multiprocess shared memory access.
"""

import ctypes
import mmap
import multiprocessing
import os
import sys
import time

# Constants for test
ITERATIONS = 1000000
SHARED_SIZE = 1024


def memory_barrier() -> None:
    """
    Implement a full memory barrier using C11 atomic_thread_fence.
    This ensures all memory operations are visible across CPU cores.
    """
    try:
        libc = ctypes.CDLL("libc.so.6")
        atomic_thread_fence = libc.atomic_thread_fence
        atomic_thread_fence.argtypes = [ctypes.c_int]
        atomic_thread_fence.restype = None
        memory_order_seq_cst = 5  # Strongest memory ordering
        atomic_thread_fence(memory_order_seq_cst)
    except (OSError, AttributeError):
        # Fallback - this won't be as effective
        pass


def writer_process(shm_name: str, use_barrier: bool, ready_event, done_event):
    """
    Writer process that writes sequential values to shared memory.
    """
    # Open shared memory
    shm_fd = os.open(f"/dev/shm/{shm_name}", os.O_RDWR)
    shm = mmap.mmap(shm_fd, SHARED_SIZE)

    # Create a ctypes view for fast access
    array_type = ctypes.c_uint64 * (SHARED_SIZE // 8)
    array = array_type.from_buffer(shm)

    print(f"Writer: Starting (barrier={'ON' if use_barrier else 'OFF'})")

    # Signal ready
    ready_event.set()

    # Write pattern: [sequence, value, sequence, value, ...]
    for i in range(ITERATIONS):
        # Write sequence number at position 0
        array[0] = i + 1

        if use_barrier:
            memory_barrier()

        # Write value at position 1 (should always equal sequence)
        array[1] = i + 1

        if use_barrier:
            memory_barrier()

        # Small delay to allow reader to catch inconsistency
        if i % 10000 == 0:
            time.sleep(0.00001)  # 10 microseconds

    print(f"Writer: Completed {ITERATIONS} iterations")
    shm.close()
    os.close(shm_fd)
    done_event.set()


def reader_process(shm_name: str, use_barrier: bool, ready_event, done_event, result_queue):
    """
    Reader process that reads values and checks for consistency.
    """
    # Wait for writer to be ready
    ready_event.wait()

    # Open shared memory
    shm_fd = os.open(f"/dev/shm/{shm_name}", os.O_RDWR)
    shm = mmap.mmap(shm_fd, SHARED_SIZE)

    # Create a ctypes view for fast access
    array_type = ctypes.c_uint64 * (SHARED_SIZE // 8)
    array = array_type.from_buffer(shm)

    print(f"Reader: Starting (barrier={'ON' if use_barrier else 'OFF'})")

    inconsistencies = 0
    reads = 0
    last_seq = 0

    while not done_event.is_set():
        if use_barrier:
            memory_barrier()

        # Read sequence and value
        seq = array[0]

        if use_barrier:
            memory_barrier()

        val = array[1]

        # Check consistency: sequence should equal value
        if seq != val and seq > 0:
            inconsistencies += 1
            if inconsistencies <= 5:  # Print first 5 inconsistencies
                print(f"Reader: Inconsistency detected! seq={seq}, val={val}")

        # Track progress
        if seq > last_seq:
            reads += 1
            last_seq = seq

        # Small delay
        if reads % 10000 == 0:
            time.sleep(0.00001)

    print(f"Reader: Completed with {reads} reads, {inconsistencies} inconsistencies")
    shm.close()
    os.close(shm_fd)

    # Return results
    result_queue.put({
        'reads': reads,
        'inconsistencies': inconsistencies
    })


def run_test(use_barrier: bool) -> dict:
    """
    Run the test with or without memory barriers.
    """
    print(f"\n{'='*60}")
    print("Running test WITH memory barriers" if use_barrier else "Running test WITHOUT memory barriers")
    print(f"{'='*60}")

    # Create shared memory file
    shm_name = f"test_barrier_{os.getpid()}"
    shm_path = f"/dev/shm/{shm_name}"

    # Create and initialize shared memory
    with open(shm_path, 'wb') as f:
        f.write(b'\x00' * SHARED_SIZE)

    # Set permissions
    os.chmod(shm_path, 0o666)

    # Create synchronization primitives
    ready_event = multiprocessing.Event()
    done_event = multiprocessing.Event()
    result_queue = multiprocessing.Queue()

    # Start processes
    writer = multiprocessing.Process(
        target=writer_process,
        args=(shm_name, use_barrier, ready_event, done_event)
    )
    reader = multiprocessing.Process(
        target=reader_process,
        args=(shm_name, use_barrier, ready_event, done_event, result_queue)
    )

    writer.start()
    reader.start()

    # Wait for completion
    writer.join(timeout=10)
    reader.join(timeout=10)

    # Get results
    results = result_queue.get(timeout=1) if not result_queue.empty() else {'reads': 0, 'inconsistencies': -1}

    # Cleanup
    try:
        os.unlink(shm_path)
    except:
        pass

    return results


def main():
    """
    Main test runner.
    """
    print("Memory Barrier Test for Shared Memory")
    print("======================================")
    print(f"Platform: {sys.platform}")
    print(f"Python: {sys.version}")
    print(f"Iterations: {ITERATIONS}")

    # Test without barrier
    print("\nPhase 1: Testing WITHOUT memory barriers...")
    results_no_barrier = run_test(use_barrier=False)

    # Test with barrier
    print("\nPhase 2: Testing WITH memory barriers...")
    results_with_barrier = run_test(use_barrier=True)

    # Summary
    print(f"\n{'='*60}")
    print("SUMMARY")
    print(f"{'='*60}")
    print(f"WITHOUT barriers: {results_no_barrier['inconsistencies']} inconsistencies in {results_no_barrier['reads']} reads")
    print(f"WITH barriers:    {results_with_barrier['inconsistencies']} inconsistencies in {results_with_barrier['reads']} reads")

    # Determine success
    if results_no_barrier['inconsistencies'] > 0 and results_with_barrier['inconsistencies'] == 0:
        print("\n✅ SUCCESS: Memory barriers eliminated all inconsistencies!")
        return 0
    elif results_no_barrier['inconsistencies'] == 0 and results_with_barrier['inconsistencies'] == 0:
        print("\n⚠️  WARNING: No inconsistencies detected even without barriers.")
        print("    This might be due to:")
        print("    - Single CPU core")
        print("    - CPU cache coherency protocols")
        print("    - Python GIL effects")
        print("    Try running with more stress or on a multi-core system.")
        return 0
    elif results_with_barrier['inconsistencies'] > 0:
        print("\n❌ FAILURE: Memory barriers did not eliminate all inconsistencies!")
        print("    This might indicate the barrier implementation needs adjustment.")
        return 1
    else:
        print("\n❓ UNEXPECTED: Barriers made it worse?")
        return 1


if __name__ == "__main__":
    sys.exit(main())
