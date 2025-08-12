#ifndef ROCKET_WELDER_CLIENT_HPP
#define ROCKET_WELDER_CLIENT_HPP

#include <opencv2/opencv.hpp>
#include <functional>
#include <memory>
#include <string>

namespace rocket_welder {

class Client {
public:
    using FrameCallback = std::function<void(cv::Mat&)>;
    
    // Factory methods
    static std::unique_ptr<Client> from(int argc, char* argv[]);
    static std::unique_ptr<Client> from_env();
    static std::unique_ptr<Client> from_connection_string(const std::string& connection_string);
    
    // Destructor
    virtual ~Client() = default;
    
    // Set frame processing callback
    virtual void on_frame(FrameCallback callback) = 0;
    
    // Start processing
    virtual void start() = 0;
    
    // Stop processing
    virtual void stop() = 0;
    
    // Check if running
    virtual bool is_running() const = 0;
};

} // namespace rocket_welder

#endif // ROCKET_WELDER_CLIENT_HPP