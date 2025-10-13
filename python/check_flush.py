import mmap
import os
import time

# Create a file-backed mmap to test
with open('/tmp/test_mmap', 'wb') as f:
    f.write(b'\x00' * 4096)

# Open and map it
f = open('/tmp/test_mmap', 'r+b')
m = mmap.mmap(f.fileno(), 4096)

print("Writing to mmap...")
m[0:4] = b'TEST'

print("Before flush - data in memory but maybe not on disk")
# At this point, data is in memory but might not be on disk

print("Calling flush()...")
m.flush()  # This calls msync(addr, length, MS_SYNC)
print("After flush - data guaranteed to be on disk")

# For POSIX shared memory (not file-backed):
# flush() still calls msync() but it may be a no-op since 
# shared memory is already coherent in RAM

m.close()
f.close()
os.unlink('/tmp/test_mmap')
