#include "rocket_welder/client.hpp"
#include <cstdlib>
#include <iostream>
#include <thread>

namespace rocket_welder {

namespace {

// Internal implementation class
class ClientImpl : public Client {
private:
    FrameCallback callback_;
    std::string connection_string_;
    bool running_ = false;
    std::thread worker_thread_;
    
public:
    explicit ClientImpl(const std::string& connection_string) 
        : connection_string_(connection_string) {
        // TODO: Parse connection string
        // TODO: Initialize based on protocol (shm://, mjpeg+http://, etc.)
    }
    
    ~ClientImpl() override {
        stop();
    }
    
    void on_frame(FrameCallback callback) override {
        callback_ = callback;
    }
    
    void start() override {
        if (running_) return;
        
        running_ = true;
        
        // TODO: Implement actual frame processing
        // For now, just a placeholder that generates dummy frames
        worker_thread_ = std::thread([this]() {
            while (running_) {
                if (callback_) {
                    // Create dummy frame for testing
                    cv::Mat frame(480, 640, CV_8UC3, cv::Scalar(0, 0, 0));
                    callback_(frame);
                }
                std::this_thread::sleep_for(std::chrono::milliseconds(33)); // ~30 FPS
            }
        });
    }
    
    void stop() override {
        if (!running_) return;
        
        running_ = false;
        if (worker_thread_.joinable()) {
            worker_thread_.join();
        }
    }
    
    bool is_running() const override {
        return running_;
    }
};

// Helper function to get connection string from environment or args
std::string get_connection_string(int argc, char* argv[]) {
    // First check environment variable
    const char* env_conn_str = std::getenv("CONNECTION_STRING");
    if (env_conn_str) {
        // Check if overridden by command line args
        for (int i = 1; i < argc; ++i) {
            std::string arg(argv[i]);
            if (arg.find("shm://") == 0 || 
                arg.find("mjpeg+http://") == 0 || 
                arg.find("mjpeg+tcp://") == 0) {
                return arg;
            }
        }
        return std::string(env_conn_str);
    }
    
    // Look for connection string in args
    for (int i = 1; i < argc; ++i) {
        std::string arg(argv[i]);
        if (arg.find("shm://") == 0 || 
            arg.find("mjpeg+http://") == 0 || 
            arg.find("mjpeg+tcp://") == 0) {
            return arg;
        }
    }
    
    // Default
    return "shm://default";
}

} // anonymous namespace

// Factory method implementations
std::unique_ptr<Client> Client::from(int argc, char* argv[]) {
    std::string connection_string = get_connection_string(argc, argv);
    return std::make_unique<ClientImpl>(connection_string);
}

std::unique_ptr<Client> Client::from_env() {
    const char* env_conn_str = std::getenv("CONNECTION_STRING");
    std::string connection_string = env_conn_str ? env_conn_str : "shm://default";
    return std::make_unique<ClientImpl>(connection_string);
}

std::unique_ptr<Client> Client::from_connection_string(const std::string& connection_string) {
    return std::make_unique<ClientImpl>(connection_string);
}

} // namespace rocket_welder