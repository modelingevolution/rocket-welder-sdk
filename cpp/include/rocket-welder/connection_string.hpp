#pragma once

#include <string>
#include <string_view>
#include <optional>
#include <cstdint>
#include <sstream>
#include <stdexcept>
#include <algorithm>

namespace rocket_welder {

enum class Protocol : uint32_t {
    None = 0,
    Shm = 1 << 0,
    Mjpeg = 1 << 1,
    Http = 1 << 2,
    Tcp = 1 << 3
};

inline Protocol operator|(Protocol a, Protocol b) {
    return static_cast<Protocol>(static_cast<uint32_t>(a) | static_cast<uint32_t>(b));
}

inline Protocol operator&(Protocol a, Protocol b) {
    return static_cast<Protocol>(static_cast<uint32_t>(a) & static_cast<uint32_t>(b));
}

inline Protocol operator+(Protocol a, Protocol b) {
    return a | b;  // + maps to | as per C# requirement
}

inline bool has_flag(Protocol value, Protocol flag) {
    return (value & flag) == flag;
}

struct ConnectionString {
    Protocol protocol = Protocol::None;
    std::optional<std::string> host;
    std::optional<uint16_t> port;
    std::optional<std::string> path;
    std::optional<std::string> buffer_name;
    size_t buffer_size = 10485760;  // 10MB default
    size_t metadata_size = 65536;   // 64KB default
    std::string mode = "oneway";    // "oneway" or "duplex"
    
    static ConnectionString parse(std::string_view connection_str);
    static std::optional<ConnectionString> try_parse(std::string_view connection_str);
    
    std::string to_string() const;
    
private:
    static std::string_view extract_scheme(std::string_view& url);
    static std::string_view extract_authority(std::string_view& url);
    static std::string_view extract_path(std::string_view& url);
    static std::string_view extract_query(std::string_view& url);
    static Protocol parse_protocol(std::string_view scheme);
};

inline ConnectionString ConnectionString::parse(std::string_view connection_str) {
    if (connection_str.empty()) {
        throw std::invalid_argument("Connection string cannot be empty");
    }
    
    ConnectionString result;
    std::string_view url = connection_str;
    
    // Parse scheme
    auto scheme = extract_scheme(url);
    result.protocol = parse_protocol(scheme);
    
    // Parse based on protocol
    if (result.protocol == Protocol::Shm) {
        // For SHM, the authority or path becomes the buffer name
        auto authority = extract_authority(url);
        if (!authority.empty()) {
            result.buffer_name = std::string(authority);
        } else {
            auto path = extract_path(url);
            if (!path.empty() && path[0] == '/') {
                path.remove_prefix(1);
            }
            result.buffer_name = path.empty() ? "default" : std::string(path);
        }
    } else {
        // For network protocols
        auto authority = extract_authority(url);
        
        // Parse host:port
        auto colon_pos = authority.find(':');
        if (colon_pos != std::string_view::npos) {
            result.host = std::string(authority.substr(0, colon_pos));
            auto port_str = authority.substr(colon_pos + 1);
            if (!port_str.empty()) {
                result.port = static_cast<uint16_t>(std::stoi(std::string(port_str)));
            }
        } else {
            result.host = std::string(authority);
        }
        
        // Parse path
        auto path = extract_path(url);
        if (!path.empty() && path[0] == '/') {
            path.remove_prefix(1);
        }
        if (!path.empty()) {
            result.path = std::string(path);
        }
    }
    
    // Parse query parameters
    auto query = extract_query(url);
    while (!query.empty()) {
        auto amp_pos = query.find('&');
        auto param = query.substr(0, amp_pos);
        
        auto eq_pos = param.find('=');
        if (eq_pos != std::string_view::npos) {
            auto key = param.substr(0, eq_pos);
            auto value = param.substr(eq_pos + 1);
            
            if (key == "buffer_size") {
                result.buffer_size = std::stoull(std::string(value));
            } else if (key == "metadata_size") {
                result.metadata_size = std::stoull(std::string(value));
            } else if (key == "mode") {
                result.mode = std::string(value);
            }
        }
        
        if (amp_pos == std::string_view::npos) {
            break;
        }
        query.remove_prefix(amp_pos + 1);
    }
    
    return result;
}

inline std::optional<ConnectionString> ConnectionString::try_parse(std::string_view connection_str) {
    try {
        return parse(connection_str);
    } catch (...) {
        return std::nullopt;
    }
}

inline std::string ConnectionString::to_string() const {
    std::ostringstream ss;
    
    if (protocol == Protocol::Shm) {
        ss << "shm://" << buffer_name.value_or("default");
        ss << "?buffer_size=" << buffer_size;
        ss << "&metadata_size=" << metadata_size;
        ss << "&mode=" << mode;
    } else if (protocol == (Protocol::Mjpeg | Protocol::Http)) {
        ss << "mjpeg+http://" << host.value_or("");
        if (port.has_value()) {
            ss << ":" << *port;
        }
        if (path.has_value()) {
            ss << "/" << *path;
        }
    } else if (protocol == (Protocol::Mjpeg | Protocol::Tcp)) {
        ss << "mjpeg+tcp://" << host.value_or("");
        if (port.has_value()) {
            ss << ":" << *port;
        }
        if (path.has_value()) {
            ss << "/" << *path;
        }
    }
    
    return ss.str();
}

inline std::string_view ConnectionString::extract_scheme(std::string_view& url) {
    auto pos = url.find("://");
    if (pos == std::string_view::npos) {
        throw std::invalid_argument("Invalid URL format: missing scheme");
    }
    auto scheme = url.substr(0, pos);
    url.remove_prefix(pos + 3);
    return scheme;
}

inline std::string_view ConnectionString::extract_authority(std::string_view& url) {
    auto slash_pos = url.find('/');
    auto question_pos = url.find('?');
    auto end_pos = std::min(slash_pos, question_pos);
    
    auto authority = url.substr(0, end_pos);
    if (end_pos != std::string_view::npos) {
        url.remove_prefix(end_pos);
    } else {
        url = std::string_view();
    }
    return authority;
}

inline std::string_view ConnectionString::extract_path(std::string_view& url) {
    if (url.empty() || url[0] != '/') {
        return std::string_view();
    }
    
    auto question_pos = url.find('?');
    auto path = url.substr(0, question_pos);
    
    if (question_pos != std::string_view::npos) {
        url.remove_prefix(question_pos);
    } else {
        url = std::string_view();
    }
    return path;
}

inline std::string_view ConnectionString::extract_query(std::string_view& url) {
    if (url.empty() || url[0] != '?') {
        return std::string_view();
    }
    url.remove_prefix(1);
    return url;
}

inline Protocol ConnectionString::parse_protocol(std::string_view scheme) {
    if (scheme == "shm") {
        return Protocol::Shm;
    } else if (scheme == "mjpeg+http") {
        return Protocol::Mjpeg | Protocol::Http;
    } else if (scheme == "mjpeg+tcp") {
        return Protocol::Mjpeg | Protocol::Tcp;
    } else if (scheme == "http") {
        return Protocol::Http;
    } else if (scheme == "tcp") {
        return Protocol::Tcp;
    } else {
        throw std::invalid_argument("Unknown protocol: " + std::string(scheme));
    }
}

} // namespace rocket_welder