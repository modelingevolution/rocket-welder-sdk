#!/usr/bin/env python3
"""Check OIEB structure in shared memory buffer"""

import sys
import posix_ipc
import mmap
import struct

def check_oieb(buffer_name):
    """Read and display OIEB structure from shared memory"""
    try:
        # Open shared memory
        shm = posix_ipc.SharedMemory(buffer_name)
        
        # Map it to memory
        mem = mmap.mmap(shm.fd, shm.size)
        
        # Read first 128 bytes (OIEB)
        oieb_data = mem[:128]
        
        # Parse OIEB fields
        oieb_size = struct.unpack('<I', oieb_data[0:4])[0]
        version = struct.unpack('4B', oieb_data[4:8])
        metadata_size = struct.unpack('<Q', oieb_data[8:16])[0]
        metadata_free_bytes = struct.unpack('<Q', oieb_data[16:24])[0]
        metadata_written_bytes = struct.unpack('<Q', oieb_data[24:32])[0]
        payload_size = struct.unpack('<Q', oieb_data[32:40])[0]
        payload_free_bytes = struct.unpack('<Q', oieb_data[40:48])[0]
        payload_write_pos = struct.unpack('<Q', oieb_data[48:56])[0]
        payload_read_pos = struct.unpack('<Q', oieb_data[56:64])[0]
        payload_written_count = struct.unpack('<Q', oieb_data[64:72])[0]
        payload_read_count = struct.unpack('<Q', oieb_data[72:80])[0]
        writer_pid = struct.unpack('<Q', oieb_data[80:88])[0]
        reader_pid = struct.unpack('<Q', oieb_data[88:96])[0]
        
        print(f"Buffer: {buffer_name}")
        print(f"OIEB size field: {oieb_size} (should be 128)")
        print(f"Actual OIEB size: {len(oieb_data)} bytes")
        print(f"Version: {version[0]}.{version[1]}.{version[2]} (reserved: {version[3]})")
        print(f"Metadata size: {metadata_size}")
        print(f"Payload size: {payload_size}")
        print(f"Writer PID: {writer_pid}")
        print(f"Reader PID: {reader_pid}")
        print(f"Written count: {payload_written_count}")
        print(f"Read count: {payload_read_count}")
        print("")
        print("First 128 bytes (hex):")
        for i in range(0, 128, 16):
            hex_str = ' '.join(f'{b:02x}' for b in oieb_data[i:i+16])
            print(f"  {i:03d}: {hex_str}")
        
        # Clean up
        mem.close()
        shm.close_fd()
        
    except Exception as e:
        print(f"Error reading buffer '{buffer_name}': {e}")
        sys.exit(1)

if __name__ == "__main__":
    if len(sys.argv) != 2:
        print("Usage: python check_oieb.py <buffer_name>")
        sys.exit(1)
    
    check_oieb(sys.argv[1])