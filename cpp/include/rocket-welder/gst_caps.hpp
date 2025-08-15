#pragma once

#include <string>
#include <string_view>
#include <optional>
#include <cstdint>
#include <sstream>
#include <stdexcept>
#include <regex>
#include <opencv2/core.hpp>

namespace rocket_welder {

struct GstCaps {
    int width;
    int height;
    std::string format;
    std::optional<std::pair<int, int>> framerate;
    
    static GstCaps parse(std::string_view caps_str);
    static std::optional<GstCaps> try_parse(std::string_view caps_str);
    static GstCaps from_simple(int width, int height, const std::string& format = "RGB");
    
    cv::Mat create_mat(void* data_ptr) const;
    cv::Mat create_mat_from_buffer(const uint8_t* buffer, size_t size) const;
    
    int get_opencv_type() const;
    int get_channels() const;
    size_t get_bytes_per_pixel() const;
    size_t get_frame_size() const;
    
    std::string to_string() const;
    
private:
    static std::string remove_type_annotation(const std::string& value);
};

inline GstCaps GstCaps::parse(std::string_view caps_str) {
    if (caps_str.empty()) {
        throw std::invalid_argument("Caps string cannot be empty");
    }
    
    GstCaps result;
    std::string caps(caps_str);
    
    // Remove video/x-raw prefix if present
    const std::string prefix = "video/x-raw";
    if (caps.starts_with(prefix)) {
        caps = caps.substr(prefix.length());
        if (!caps.empty() && caps[0] == ',') {
            caps = caps.substr(1);
        }
    }
    
    // Parse key=value pairs
    std::regex param_regex("([^=,]+)=([^,]+)");
    std::smatch match;
    std::string::const_iterator search_start(caps.cbegin());
    
    bool has_width = false;
    bool has_height = false;
    
    while (std::regex_search(search_start, caps.cend(), match, param_regex)) {
        std::string key = match[1];
        std::string value = match[2];
        
        // Trim whitespace
        key.erase(0, key.find_first_not_of(" \t"));
        key.erase(key.find_last_not_of(" \t") + 1);
        value.erase(0, value.find_first_not_of(" \t"));
        value.erase(value.find_last_not_of(" \t") + 1);
        
        if (key == "width") {
            value = remove_type_annotation(value);
            result.width = std::stoi(value);
            has_width = true;
        } else if (key == "height") {
            value = remove_type_annotation(value);
            result.height = std::stoi(value);
            has_height = true;
        } else if (key == "format") {
            value = remove_type_annotation(value);
            result.format = value;
        } else if (key == "framerate") {
            value = remove_type_annotation(value);
            std::regex fr_regex("(\\d+)/(\\d+)");
            std::smatch fr_match;
            if (std::regex_match(value, fr_match, fr_regex)) {
                int num = std::stoi(fr_match[1]);
                int denom = std::stoi(fr_match[2]);
                result.framerate = std::make_pair(num, denom);
            }
        }
        
        search_start = match.suffix().first;
    }
    
    if (!has_width) {
        throw std::invalid_argument("Missing 'width' in caps");
    }
    if (!has_height) {
        throw std::invalid_argument("Missing 'height' in caps");
    }
    
    if (result.format.empty()) {
        result.format = "RGB";
    }
    
    return result;
}

inline std::optional<GstCaps> GstCaps::try_parse(std::string_view caps_str) {
    try {
        return parse(caps_str);
    } catch (...) {
        return std::nullopt;
    }
}

inline GstCaps GstCaps::from_simple(int width, int height, const std::string& format) {
    GstCaps result;
    result.width = width;
    result.height = height;
    result.format = format;
    return result;
}

inline cv::Mat GstCaps::create_mat(void* data_ptr) const {
    int type = get_opencv_type();
    
    // Create Mat that wraps existing data (zero-copy)
    if (get_channels() == 1) {
        return cv::Mat(height, width, type, data_ptr);
    } else {
        return cv::Mat(height, width, type, data_ptr);
    }
}

inline cv::Mat GstCaps::create_mat_from_buffer(const uint8_t* buffer, size_t size) const {
    int type = get_opencv_type();
    size_t expected_size = get_frame_size();
    
    if (size < expected_size) {
        throw std::invalid_argument("Buffer size too small for frame dimensions");
    }
    
    // Create Mat that wraps existing buffer (zero-copy if buffer is kept alive)
    return cv::Mat(height, width, type, const_cast<uint8_t*>(buffer));
}

inline int GstCaps::get_opencv_type() const {
    int channels = get_channels();
    
    // Check for 16-bit formats
    if (format.find("16") != std::string::npos) {
        switch (channels) {
            case 1: return CV_16UC1;
            case 3: return CV_16UC3;
            case 4: return CV_16UC4;
            default: return CV_16UC3;
        }
    }
    
    // Default to 8-bit
    switch (channels) {
        case 1: return CV_8UC1;
        case 3: return CV_8UC3;
        case 4: return CV_8UC4;
        default: return CV_8UC3;
    }
}

inline int GstCaps::get_channels() const {
    if (format == "RGB" || format == "BGR") {
        return 3;
    } else if (format == "RGBA" || format == "BGRA") {
        return 4;
    } else if (format.starts_with("GRAY")) {
        return 1;
    } else {
        return 3;  // Default to 3 channels
    }
}

inline size_t GstCaps::get_bytes_per_pixel() const {
    int channels = get_channels();
    int bytes_per_channel = (format.find("16") != std::string::npos) ? 2 : 1;
    return channels * bytes_per_channel;
}

inline size_t GstCaps::get_frame_size() const {
    return width * height * get_bytes_per_pixel();
}

inline std::string GstCaps::to_string() const {
    std::ostringstream ss;
    ss << "video/x-raw,format=" << format 
       << ",width=" << width 
       << ",height=" << height;
    
    if (framerate.has_value()) {
        ss << ",framerate=" << framerate->first << "/" << framerate->second;
    }
    
    return ss.str();
}

inline std::string GstCaps::remove_type_annotation(const std::string& value) {
    // Remove GStreamer type annotations like "(int)640", "(string)RGB", "(fraction)30/1"
    std::string result = value;
    
    if (result.starts_with("(int)")) {
        result = result.substr(5);
    } else if (result.starts_with("(string)")) {
        result = result.substr(8);
    } else if (result.starts_with("(fraction)")) {
        result = result.substr(10);
    }
    
    return result;
}

} // namespace rocket_welder