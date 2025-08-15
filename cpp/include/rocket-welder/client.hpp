#pragma once

#include "connection_string.hpp"
#include "gst_caps.hpp"
#include <zerobuffer/reader.h>
#include <zerobuffer/writer.h>
#include <opencv2/core.hpp>
#include <opencv2/imgproc.hpp>
#include <opencv2/videoio.hpp>
#include <functional>
#include <memory>
#include <thread>
#include <atomic>
#include <optional>
#include <json/json.h>

namespace rocket_welder {

class RocketWelderClient {
public:
    using FrameCallback = std::function<void(cv::Mat& frame)>;
    
    explicit RocketWelderClient(const std::string& connection_string);
    explicit RocketWelderClient(const ConnectionString& connection);
    ~RocketWelderClient();
    
    // Static factory methods
    static std::unique_ptr<RocketWelderClient> from_args(int argc, char* argv[]);
    static std::unique_ptr<RocketWelderClient> from_environment();
    static std::unique_ptr<RocketWelderClient> from_connection_string(const std::string& connection_str);
    
    // Properties
    const ConnectionString& connection() const { return connection_; }
    bool is_running() const { return running_.load(); }
    
    // Frame processing
    void on_frame(FrameCallback callback);
    void start();
    void stop();
    
private:
    ConnectionString connection_;
    FrameCallback frame_callback_;
    std::atomic<bool> running_{false};
    std::atomic<bool> stop_requested_{false};
    std::unique_ptr<std::thread> processing_thread_;
    
    // ZeroBuffer components
    std::unique_ptr<zerobuffer::Reader> reader_;
    std::unique_ptr<zerobuffer::Writer> writer_;
    
    // Cached video format from metadata
    std::optional<GstCaps> video_format_;
    
    // Processing methods
    void process_shared_memory();
    void process_mjpeg_http();
    void process_mjpeg_tcp();
    void process_mjpeg_stream(const std::string& url);
    
    // Helper methods
    void parse_metadata();
    std::string get_environment_variable(const std::string& name) const;
};

// Implementation

inline RocketWelderClient::RocketWelderClient(const std::string& connection_string)
    : connection_(ConnectionString::parse(connection_string)) {
}

inline RocketWelderClient::RocketWelderClient(const ConnectionString& connection)
    : connection_(connection) {
}

inline RocketWelderClient::~RocketWelderClient() {
    stop();
}

inline std::unique_ptr<RocketWelderClient> RocketWelderClient::from_args(int argc, char* argv[]) {
    // Check environment variable first
    const char* env_conn = std::getenv("CONNECTION_STRING");
    std::string connection_string = env_conn ? env_conn : "";
    
    // Override with command line args if present
    for (int i = 1; i < argc; ++i) {
        std::string arg = argv[i];
        if (arg.starts_with("shm://") || 
            arg.starts_with("mjpeg+http://") || 
            arg.starts_with("mjpeg+tcp://")) {
            connection_string = arg;
            break;
        }
    }
    
    if (connection_string.empty()) {
        connection_string = "shm://default";
    }
    
    return std::make_unique<RocketWelderClient>(connection_string);
}

inline std::unique_ptr<RocketWelderClient> RocketWelderClient::from_environment() {
    const char* env_conn = std::getenv("CONNECTION_STRING");
    std::string connection_string = env_conn ? env_conn : "shm://default";
    return std::make_unique<RocketWelderClient>(connection_string);
}

inline std::unique_ptr<RocketWelderClient> RocketWelderClient::from_connection_string(const std::string& connection_str) {
    return std::make_unique<RocketWelderClient>(connection_str);
}

inline void RocketWelderClient::on_frame(FrameCallback callback) {
    if (!callback) {
        throw std::invalid_argument("Frame callback cannot be null");
    }
    frame_callback_ = callback;
}

inline void RocketWelderClient::start() {
    if (running_.load()) {
        return;
    }
    
    if (!frame_callback_) {
        throw std::runtime_error("Frame callback must be set before starting");
    }
    
    running_ = true;
    stop_requested_ = false;
    
    // Start processing thread based on protocol
    if (connection_.protocol == Protocol::Shm) {
        processing_thread_ = std::make_unique<std::thread>(&RocketWelderClient::process_shared_memory, this);
    } else if (connection_.protocol == (Protocol::Mjpeg | Protocol::Http)) {
        processing_thread_ = std::make_unique<std::thread>(&RocketWelderClient::process_mjpeg_http, this);
    } else if (connection_.protocol == (Protocol::Mjpeg | Protocol::Tcp)) {
        processing_thread_ = std::make_unique<std::thread>(&RocketWelderClient::process_mjpeg_tcp, this);
    } else {
        running_ = false;
        throw std::runtime_error("Unsupported protocol");
    }
}

inline void RocketWelderClient::stop() {
    if (!running_.load()) {
        return;
    }
    
    stop_requested_ = true;
    
    if (processing_thread_ && processing_thread_->joinable()) {
        processing_thread_->join();
    }
    
    reader_.reset();
    writer_.reset();
    
    running_ = false;
}

inline void RocketWelderClient::process_shared_memory() {
    try {
        std::string buffer_name = connection_.buffer_name.value_or("default");
        size_t buffer_size = connection_.buffer_size;
        size_t metadata_size = connection_.metadata_size;
        
        zerobuffer::BufferConfig config;
        config.metadata_size = metadata_size;
        config.payload_size = buffer_size;
        
        // Create reader - this creates the shared memory buffer
        reader_ = std::make_unique<zerobuffer::Reader>(buffer_name, config);
        
        if (connection_.mode == "duplex") {
            // For duplex, we'd need a separate buffer for writing back
            // For now, we'll just read
        }
        
        while (!stop_requested_.load()) {
            try {
                // Read frame from shared memory (zero-copy)
                auto frame = reader_->read_frame(1000);  // 1 second timeout
                
                if (!frame.is_valid()) {
                    continue;
                }
                
                // Parse metadata on first frame or when not yet parsed
                if (!video_format_.has_value()) {
                    parse_metadata();
                }
                
                if (!video_format_.has_value()) {
                    throw std::runtime_error("No video format detected");
                }
                
                // Create Mat from the raw data using zero-copy pointer
                cv::Mat mat = video_format_->create_mat(frame.data());
                
                // Call the frame callback with zero-copy Mat
                frame_callback_(mat);
                
            } catch (const zerobuffer::WriterDeadException&) {
                // Writer process died
                break;
            } catch (const std::exception& e) {
                // Log error and continue
                std::this_thread::sleep_for(std::chrono::milliseconds(100));
            }
        }
    } catch (const std::exception& e) {
        // Log error
    }
}

inline void RocketWelderClient::parse_metadata() {
    try {
        size_t metadata_size = reader_->get_metadata_size();
        if (metadata_size <= 4) {
            return;
        }
        
        const uint8_t* metadata_ptr = static_cast<const uint8_t*>(reader_->get_metadata_raw());
        if (!metadata_ptr) {
            return;
        }
        
        // Skip GStreamer's 4-byte size prefix (little-endian)
        uint32_t json_size = *reinterpret_cast<const uint32_t*>(metadata_ptr);
        
        if (json_size == 0 || json_size > metadata_size - 4) {
            return;
        }
        
        // Parse JSON metadata
        std::string json_str(reinterpret_cast<const char*>(metadata_ptr + 4), json_size);
        
        Json::CharReaderBuilder builder;
        Json::Value root;
        std::string errors;
        std::istringstream stream(json_str);
        
        if (!Json::parseFromStream(builder, stream, &root, &errors)) {
            return;
        }
        
        // Try to parse from caps string first
        if (root.isMember("caps")) {
            std::string caps = root["caps"].asString();
            if (!caps.empty()) {
                video_format_ = GstCaps::parse(caps);
                return;
            }
        }
        
        // Fallback to individual properties
        if (root.isMember("width") && root.isMember("height")) {
            int width = root["width"].asInt();
            int height = root["height"].asInt();
            std::string format = root.get("format", "RGB").asString();
            
            video_format_ = GstCaps::from_simple(width, height, format);
        }
    } catch (...) {
        // Ignore metadata parsing errors
    }
}

inline void RocketWelderClient::process_mjpeg_http() {
    std::ostringstream url;
    url << "http://" << connection_.host.value_or("");
    if (connection_.port.has_value()) {
        url << ":" << *connection_.port;
    } else {
        url << ":80";
    }
    if (connection_.path.has_value()) {
        url << "/" << *connection_.path;
    }
    process_mjpeg_stream(url.str());
}

inline void RocketWelderClient::process_mjpeg_tcp() {
    std::ostringstream url;
    url << "tcp://" << connection_.host.value_or("");
    if (connection_.port.has_value()) {
        url << ":" << *connection_.port;
    } else {
        url << ":8080";
    }
    if (connection_.path.has_value()) {
        url << "/" << *connection_.path;
    }
    process_mjpeg_stream(url.str());
}

inline void RocketWelderClient::process_mjpeg_stream(const std::string& url) {
    try {
        cv::VideoCapture capture(url);
        
        if (!capture.isOpened()) {
            throw std::runtime_error("Failed to open video stream: " + url);
        }
        
        cv::Mat frame;
        
        while (!stop_requested_.load()) {
            if (capture.read(frame) && !frame.empty()) {
                frame_callback_(frame);
            } else {
                std::this_thread::sleep_for(std::chrono::milliseconds(10));
            }
        }
        
        capture.release();
    } catch (const std::exception& e) {
        // Log error
    }
}

} // namespace rocket_welder