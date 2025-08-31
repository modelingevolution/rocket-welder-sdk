/*
 * OIEB Reader - Read and validate OIEB structure from shared memory
 * Compile: g++ -o oieb_reader oieb_reader.cpp -lrt
 */

#include <iostream>
#include <iomanip>
#include <cstring>
#include <fcntl.h>
#include <sys/mman.h>
#include <sys/stat.h>
#include <unistd.h>
#include <cstdint>

struct ProtocolVersion {
    uint8_t major;
    uint8_t minor;
    uint8_t patch;
    uint8_t reserved;
} __attribute__((packed));

struct OIEB {
    uint32_t oieb_size;
    ProtocolVersion version;
    uint64_t metadata_size;
    uint64_t metadata_free_bytes;
    uint64_t metadata_written_bytes;
    uint64_t payload_size;
    uint64_t payload_free_bytes;
    uint64_t payload_write_pos;
    uint64_t payload_read_pos;
    uint64_t payload_written_count;
    uint64_t payload_read_count;
    uint64_t writer_pid;
    uint64_t reader_pid;
    uint64_t reserved[4];
} __attribute__((packed));

void print_hex_dump(const uint8_t* data, size_t size) {
    for (size_t i = 0; i < size; i += 16) {
        std::cout << "  " << std::setfill('0') << std::setw(3) << i << ": ";
        for (size_t j = 0; j < 16 && i + j < size; ++j) {
            std::cout << std::setfill('0') << std::setw(2) << std::hex 
                     << static_cast<int>(data[i + j]) << " ";
        }
        std::cout << std::dec << std::endl;
    }
}

int main(int argc, char* argv[]) {
    if (argc != 2) {
        std::cerr << "Usage: " << argv[0] << " <buffer_name>" << std::endl;
        return 1;
    }

    const char* buffer_name = argv[1];
    std::string shm_name = "/" + std::string(buffer_name);
    
    // Open shared memory
    int fd = shm_open(shm_name.c_str(), O_RDONLY, 0);
    if (fd == -1) {
        std::cerr << "Error: Failed to open shared memory '" << shm_name << "': " 
                  << strerror(errno) << std::endl;
        return 1;
    }

    // Get size of shared memory
    struct stat shm_stat;
    if (fstat(fd, &shm_stat) == -1) {
        std::cerr << "Error: Failed to get shared memory size: " 
                  << strerror(errno) << std::endl;
        close(fd);
        return 1;
    }

    // Map shared memory
    void* addr = mmap(nullptr, shm_stat.st_size, PROT_READ, MAP_SHARED, fd, 0);
    if (addr == MAP_FAILED) {
        std::cerr << "Error: Failed to map shared memory: " 
                  << strerror(errno) << std::endl;
        close(fd);
        return 1;
    }

    // Read OIEB structure
    OIEB* oieb = static_cast<OIEB*>(addr);
    
    std::cout << "Buffer: " << buffer_name << std::endl;
    std::cout << "Shared memory size: " << shm_stat.st_size << " bytes" << std::endl;
    std::cout << std::endl;
    
    std::cout << "=== OIEB Structure ===" << std::endl;
    std::cout << "OIEB size field: " << oieb->oieb_size << " (should be 128)" << std::endl;
    std::cout << "Actual struct size: " << sizeof(OIEB) << " bytes" << std::endl;
    std::cout << "Version: " << static_cast<int>(oieb->version.major) << "."
              << static_cast<int>(oieb->version.minor) << "."
              << static_cast<int>(oieb->version.patch) 
              << " (reserved: " << static_cast<int>(oieb->version.reserved) << ")" << std::endl;
    std::cout << std::endl;
    
    std::cout << "=== Metadata ===" << std::endl;
    std::cout << "Metadata size: " << oieb->metadata_size << " bytes" << std::endl;
    std::cout << "Metadata free: " << oieb->metadata_free_bytes << " bytes" << std::endl;
    std::cout << "Metadata written: " << oieb->metadata_written_bytes << " bytes" << std::endl;
    std::cout << std::endl;
    
    std::cout << "=== Payload ===" << std::endl;
    std::cout << "Payload size: " << oieb->payload_size << " bytes" << std::endl;
    std::cout << "Payload free: " << oieb->payload_free_bytes << " bytes" << std::endl;
    std::cout << "Write position: " << oieb->payload_write_pos << std::endl;
    std::cout << "Read position: " << oieb->payload_read_pos << std::endl;
    std::cout << "Written count: " << oieb->payload_written_count << std::endl;
    std::cout << "Read count: " << oieb->payload_read_count << std::endl;
    std::cout << std::endl;
    
    std::cout << "=== Process Info ===" << std::endl;
    std::cout << "Writer PID: " << oieb->writer_pid << std::endl;
    std::cout << "Reader PID: " << oieb->reader_pid << std::endl;
    std::cout << std::endl;
    
    std::cout << "=== Validation ===" << std::endl;
    bool valid = true;
    
    if (oieb->oieb_size != 128) {
        std::cout << "ERROR: OIEB size field is " << oieb->oieb_size 
                  << " but should be 128" << std::endl;
        valid = false;
    }
    
    if (sizeof(OIEB) != 128) {
        std::cout << "ERROR: OIEB struct size is " << sizeof(OIEB) 
                  << " but should be 128" << std::endl;
        valid = false;
    }
    
    if (oieb->version.major != 1) {
        std::cout << "WARNING: Unexpected major version " 
                  << static_cast<int>(oieb->version.major) << std::endl;
    }
    
    if (oieb->payload_size == 0) {
        std::cout << "ERROR: Payload size is 0" << std::endl;
        valid = false;
    }
    
    if (oieb->metadata_size == 0) {
        std::cout << "ERROR: Metadata size is 0" << std::endl;
        valid = false;
    }
    
    if (oieb->payload_write_pos >= oieb->payload_size) {
        std::cout << "ERROR: Write position " << oieb->payload_write_pos 
                  << " >= payload size " << oieb->payload_size << std::endl;
        valid = false;
    }
    
    if (oieb->payload_read_pos >= oieb->payload_size) {
        std::cout << "ERROR: Read position " << oieb->payload_read_pos 
                  << " >= payload size " << oieb->payload_size << std::endl;
        valid = false;
    }
    
    if (valid) {
        std::cout << "✓ OIEB structure appears valid" << std::endl;
    } else {
        std::cout << "✗ OIEB structure has validation errors" << std::endl;
    }
    std::cout << std::endl;
    
    std::cout << "=== First 128 bytes (hex) ===" << std::endl;
    print_hex_dump(reinterpret_cast<const uint8_t*>(oieb), 128);
    
    // Clean up
    munmap(addr, shm_stat.st_size);
    close(fd);
    
    return valid ? 0 : 2;
}