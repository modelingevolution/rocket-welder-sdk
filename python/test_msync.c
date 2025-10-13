#include <stdio.h>
#include <sys/mman.h>
#include <fcntl.h>
#include <unistd.h>
#include <string.h>

int main() {
    // Create shared memory
    int fd = shm_open("/test_shm", O_CREAT | O_RDWR, 0666);
    ftruncate(fd, 4096);
    
    // Map it
    void* ptr = mmap(NULL, 4096, PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
    
    // Write some data
    strcpy(ptr, "Hello World");
    
    // Call msync - this is what mmap.flush() does
    printf("Calling msync()...\n");
    int result = msync(ptr, 4096, MS_SYNC);
    printf("msync returned: %d\n", result);
    
    // Cleanup
    munmap(ptr, 4096);
    shm_unlink("/test_shm");
    close(fd);
    
    return 0;
}
