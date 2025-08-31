#!/usr/bin/env python3
import sys
import os
import mmap
import struct

def check_buffer(buffer_name):
    path = f"/dev/shm/{buffer_name}"
    
    if not os.path.exists(path):
        print(f"Buffer does not exist: {path}")
        return
    
    print(f"Buffer exists: {path}")
    
    # Get file stats
    stat = os.stat(path)
    print(f"Size: {stat.st_size} bytes")
    print(f"Permissions: {oct(stat.st_mode)}")
    print(f"Owner UID: {stat.st_uid}")
    print(f"Owner GID: {stat.st_gid}")
    
    # Try to open and read OIEB
    try:
        with open(path, 'r+b') as f:
            # Map the first 128 bytes (OIEB size)
            with mmap.mmap(f.fileno(), 128, access=mmap.ACCESS_READ) as mm:
                # Read OIEB structure
                oieb_size = struct.unpack('<I', mm[0:4])[0]
                version_major = mm[4]
                version_minor = mm[5]
                version_patch = mm[6]
                metadata_size = struct.unpack('<Q', mm[8:16])[0]
                metadata_free = struct.unpack('<Q', mm[16:24])[0]
                payload_size = struct.unpack('<Q', mm[32:40])[0]
                payload_free = struct.unpack('<Q', mm[40:48])[0]
                writer_pid = struct.unpack('<I', mm[80:84])[0]
                reader_pid = struct.unpack('<I', mm[88:92])[0]
                
                print("\n=== OIEB Structure ===")
                print(f"OIEB size: {oieb_size} (should be 128)")
                print(f"Version: {version_major}.{version_minor}.{version_patch}")
                print(f"Metadata size: {metadata_size}")
                print(f"Metadata free: {metadata_free}")
                print(f"Payload size: {payload_size}")
                print(f"Payload free: {payload_free}")
                print(f"Writer PID: {writer_pid}")
                print(f"Reader PID: {reader_pid}")
                
                print("\n✓ Successfully read OIEB structure")
    except Exception as e:
        print(f"\n✗ Failed to read OIEB: {e}")
        print(f"Error type: {type(e).__name__}")

if __name__ == "__main__":
    if len(sys.argv) != 2:
        print("Usage: check_buffer.py <buffer_name>")
        sys.exit(1)
    
    check_buffer(sys.argv[1])