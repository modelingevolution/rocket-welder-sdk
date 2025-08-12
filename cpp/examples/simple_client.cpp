#include <rocket_welder/client.hpp>
#include <iostream>
#include <csignal>
#include <atomic>
#include <thread>
#include <chrono>
#include <string>

std::atomic<bool> running(true);

void signal_handler(int) {
    running = false;
}

int main(int argc, char* argv[]) {
    std::signal(SIGINT, signal_handler);
    
    // Parse exit-after parameter
    int exit_after = -1;  // -1 means run forever
    for (int i = 1; i < argc; ++i) {
        std::string arg(argv[i]);
        if (arg.find("--exit-after=") == 0) {
            exit_after = std::stoi(arg.substr(13));
        }
    }
    
    // Create client from command line args or environment
    auto client = rocket_welder::Client::from(argc, argv);
    
    int frame_count = 0;
    
    // Set up frame processing callback
    client->on_frame([&frame_count, exit_after, &running](cv::Mat& frame) {
        frame_count++;
        
        // Add overlay
        cv::putText(frame, "Processing", cv::Point(10, 30),
                    cv::FONT_HERSHEY_SIMPLEX, 1.0, cv::Scalar(0, 255, 0), 2);
        
        // Add frame counter
        cv::putText(frame, "Frame: " + std::to_string(frame_count), 
                    cv::Point(10, 60),
                    cv::FONT_HERSHEY_SIMPLEX, 0.5, cv::Scalar(255, 255, 255), 1);
        
        std::cout << "Processed frame " << frame_count 
                  << " (" << frame.cols << "x" << frame.rows << ")" << std::endl;
        
        // Check if we should exit
        if (exit_after > 0 && frame_count >= exit_after) {
            std::cout << "Reached " << exit_after << " frames, exiting..." << std::endl;
            running = false;
        }
    });
    
    // Start processing
    std::cout << "Starting client..." << std::endl;
    if (exit_after > 0) {
        std::cout << "Will exit after " << exit_after << " frames" << std::endl;
    }
    client->start();
    
    // Run until interrupted or frame limit reached
    while (running && client->is_running()) {
        std::this_thread::sleep_for(std::chrono::milliseconds(100));
    }
    
    // Clean shutdown
    std::cout << "Stopping client..." << std::endl;
    std::cout << "Total frames processed: " << frame_count << std::endl;
    client->stop();
    
    return 0;
}