#include <iostream>
#include <cstring>
#include <csignal>
#include <atomic>
#include <thread>
#include <chrono>
#include <memory>

// Include ZeroBuffer directly
#include <zerobuffer/reader.h>
#include <zerobuffer/types.h>

// Simple connection string parsing
struct SimpleConnection {
    std::string buffer_name = "default";
    size_t buffer_size = 10485760;  // 10MB
    size_t metadata_size = 65536;    // 64KB
    
    static SimpleConnection parse(const std::string& conn_str) {
        SimpleConnection result;
        
        // Parse shm://buffername
        if (conn_str.starts_with("shm://")) {
            auto name_start = conn_str.find("://") + 3;
            auto query_pos = conn_str.find('?');
            
            if (query_pos != std::string::npos) {
                result.buffer_name = conn_str.substr(name_start, query_pos - name_start);
            } else {
                result.buffer_name = conn_str.substr(name_start);
            }
            
            if (result.buffer_name.empty()) {
                result.buffer_name = "default";
            }
        }
        
        return result;
    }
};

std::atomic<bool> running(true);
std::atomic<int> frame_count(0);

void signal_handler(int) {
    std::cout << "\nReceived signal, shutting down..." << std::endl;
    running = false;
}

int main(int argc, char* argv[]) {
    std::signal(SIGINT, signal_handler);
    std::signal(SIGTERM, signal_handler);
    
    // Parse arguments
    std::string connection_string = "shm://default";
    int exit_after = -1;
    
    // Get connection string from environment or args
    const char* env_conn = std::getenv("CONNECTION_STRING");
    if (env_conn) {
        connection_string = env_conn;
    }
    
    for (int i = 1; i < argc; ++i) {
        std::string arg(argv[i]);
        if (arg.starts_with("shm://")) {
            connection_string = arg;
        } else if (arg == "--exit-after" && i + 1 < argc) {
            exit_after = std::stoi(argv[++i]);
        }
    }
    
    std::cout << "========================================" << std::endl;
    std::cout << "RocketWelder SDK C++ Minimal Client" << std::endl;
    std::cout << "========================================" << std::endl;
    std::cout << "Connection: " << connection_string << std::endl;
    if (exit_after > 0) {
        std::cout << "Will exit after " << exit_after << " frames" << std::endl;
    }
    std::cout << "========================================" << std::endl;
    
    try {
        // Parse connection
        auto conn = SimpleConnection::parse(connection_string);
        
        // Create ZeroBuffer reader
        zerobuffer::BufferConfig config(conn.metadata_size, conn.buffer_size);
        
        std::cout << "Creating shared memory buffer: " << conn.buffer_name 
                  << " (size: " << conn.buffer_size << ", metadata: " << conn.metadata_size << ")" << std::endl;
        
        zerobuffer::Reader reader(conn.buffer_name, config);
        
        std::cout << "Buffer created, waiting for frames..." << std::endl;
        std::cout << "Test with: GST_PLUGIN_PATH=/mnt/d/source/modelingevolution/streamer/src/gstreamer/zerobuffer/build "
                  << "gst-launch-1.0 videotestsrc num-buffers=" << (exit_after > 0 ? exit_after : 100)
                  << " pattern=ball ! video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! "
                  << "zerosink buffer-name=" << conn.buffer_name << " sync=false" << std::endl;
        
        // Process frames
        while (running.load()) {
            try {
                // Use scope block to ensure Frame is destroyed immediately after processing
                {
                    // Read frame with 1 second timeout
                    auto frame = reader.read_frame(std::chrono::milliseconds(1000));
                    
                    if (frame.is_valid()) {
                        int current = ++frame_count;
                        
                        std::cout << "Received frame " << current 
                                  << " (size: " << frame.size() 
                                  << ", seq: " << frame.sequence() << ")" << std::endl;
                        
                        // In a real application, we would process the frame data here
                        // For now, just count frames
                        
                        if (exit_after > 0 && current >= exit_after) {
                            std::cout << "Reached " << exit_after << " frames, exiting..." << std::endl;
                            break;
                        }
                    }
                } // Frame destructor called here, semaphore signaled immediately
            } catch (const zerobuffer::WriterDeadException&) {
                std::cout << "Writer disconnected" << std::endl;
                break;
            } catch (const std::exception& e) {
                std::cerr << "Error reading frame: " << e.what() << std::endl;
                std::this_thread::sleep_for(std::chrono::milliseconds(100));
            }
        }
        
    } catch (const std::exception& e) {
        std::cerr << "Error: " << e.what() << std::endl;
        return 1;
    }
    
    std::cout << "Total frames processed: " << frame_count.load() << std::endl;
    std::cout << "Client stopped" << std::endl;
    
    return 0;
}