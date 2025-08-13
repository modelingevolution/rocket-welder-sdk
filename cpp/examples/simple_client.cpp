#include <rocket-welder/client.hpp>
#include <iostream>
#include <csignal>
#include <atomic>
#include <thread>
#include <chrono>
#include <string>
#include <ctime>
#include <iomanip>
#include <sstream>

std::atomic<bool> running(true);
std::atomic<int> frame_count(0);
std::atomic<int> exit_after(-1);

void signal_handler(int) {
    std::cout << "\nReceived signal, shutting down..." << std::endl;
    running = false;
}

std::string get_timestamp() {
    auto now = std::chrono::system_clock::now();
    auto time_t = std::chrono::system_clock::to_time_t(now);
    std::stringstream ss;
    ss << std::put_time(std::localtime(&time_t), "%Y-%m-%d %H:%M:%S");
    return ss.str();
}

int main(int argc, char* argv[]) {
    std::signal(SIGINT, signal_handler);
    std::signal(SIGTERM, signal_handler);
    
    // Print arguments for debugging
    std::cout << "========================================" << std::endl;
    std::cout << "RocketWelder SDK SimpleClient" << std::endl;
    std::cout << "========================================" << std::endl;
    std::cout << "Arguments received: " << (argc - 1) << std::endl;
    for (int i = 1; i < argc; ++i) {
        std::cout << "  [" << (i-1) << "]: " << argv[i] << std::endl;
    }
    std::cout << "========================================" << std::endl;
    std::cout << std::endl;
    
    // Parse exit-after parameter
    for (int i = 1; i < argc; ++i) {
        std::string arg(argv[i]);
        if (arg.find("--exit-after=") == 0) {
            exit_after = std::stoi(arg.substr(13));
        } else if (arg == "--exit-after" && i + 1 < argc) {
            exit_after = std::stoi(argv[++i]);
        }
    }
    
    try {
        // Create client from command line args or environment
        auto client = rocket_welder::RocketWelderClient::from_args(argc, argv);
        
        std::cout << "Starting RocketWelder client..." << std::endl;
        std::cout << "Connection: " << client->connection().to_string() << std::endl;
        
        // Suggest GStreamer test command
        std::string buffer_name = client->connection().buffer_name.value_or("default");
        int num_buffers = exit_after > 0 ? exit_after.load() : 100;
        std::cout << "Can be tested with:\n\n\t"
                  << "gst-launch-1.0 videotestsrc num-buffers=" << num_buffers 
                  << " pattern=ball ! "
                  << "video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! "
                  << "zerosink buffer-name=" << buffer_name << " sync=false\n" 
                  << std::endl;
        
        if (exit_after > 0) {
            std::cout << "Will exit after " << exit_after << " frames" << std::endl;
        }
        
        // Set up frame processing callback
        client->on_frame([](cv::Mat& frame) {
            int current_frame = ++frame_count;
            
            // Add overlay text (modifies shared memory directly - zero-copy)
            cv::putText(frame, "Processing", cv::Point(10, 30),
                        cv::FONT_HERSHEY_SIMPLEX, 1.0, cv::Scalar(0, 255, 0), 2);
            
            // Add timestamp overlay
            cv::putText(frame, get_timestamp(), cv::Point(10, 60),
                        cv::FONT_HERSHEY_SIMPLEX, 0.5, cv::Scalar(255, 255, 255), 1);
            
            // Add frame counter
            cv::putText(frame, "Frame: " + std::to_string(current_frame), 
                        cv::Point(10, 90),
                        cv::FONT_HERSHEY_SIMPLEX, 0.5, cv::Scalar(255, 255, 255), 1);
            
            std::cout << "Processed frame " << current_frame 
                      << " (" << frame.cols << "x" << frame.rows << ")" << std::endl;
            
            // Check if we should exit
            int limit = exit_after.load();
            if (limit > 0 && current_frame >= limit) {
                std::cout << "Reached " << limit << " frames, exiting..." << std::endl;
                running = false;
            }
        });
        
        // Start processing
        client->start();
        
        // Run until interrupted or frame limit reached
        while (running.load() && client->is_running()) {
            std::this_thread::sleep_for(std::chrono::milliseconds(100));
        }
        
        // Clean shutdown
        std::cout << "Stopping client..." << std::endl;
        std::cout << "Total frames processed: " << frame_count.load() << std::endl;
        client->stop();
        
    } catch (const std::exception& e) {
        std::cerr << "Error: " << e.what() << std::endl;
        return 1;
    }
    
    return 0;
}