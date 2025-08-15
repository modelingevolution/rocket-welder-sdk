#include <rocket-welder/connection_string.hpp>
#include <rocket-welder/gst_caps.hpp>
#include <iostream>
#include <cassert>

using namespace rocket_welder;

void test_connection_string() {
    std::cout << "Testing ConnectionString..." << std::endl;
    
    // Test SHM protocol
    {
        auto conn = ConnectionString::parse("shm://mybuffer");
        assert(conn.protocol == Protocol::Shm);
        assert(conn.buffer_name.value_or("") == "mybuffer");
        std::cout << "  ✓ Parse SHM protocol" << std::endl;
    }
    
    // Test SHM with query parameters
    {
        auto conn = ConnectionString::parse("shm://test?buffer_size=1024&metadata_size=512&mode=duplex");
        assert(conn.protocol == Protocol::Shm);
        assert(conn.buffer_name.value_or("") == "test");
        assert(conn.buffer_size == 1024);
        assert(conn.metadata_size == 512);
        assert(conn.mode == "duplex");
        std::cout << "  ✓ Parse SHM with query parameters" << std::endl;
    }
    
    // Test MJPEG+HTTP
    {
        auto conn = ConnectionString::parse("mjpeg+http://localhost:8080/stream");
        assert(conn.protocol == (Protocol::Mjpeg | Protocol::Http));
        assert(conn.host.value_or("") == "localhost");
        assert(conn.port.value_or(0) == 8080);
        assert(conn.path.value_or("") == "stream");
        std::cout << "  ✓ Parse MJPEG+HTTP" << std::endl;
    }
    
    // Test Protocol + operator
    {
        Protocol combined = Protocol::Mjpeg + Protocol::Http;
        assert(combined == (Protocol::Mjpeg | Protocol::Http));
        std::cout << "  ✓ Protocol + operator" << std::endl;
    }
    
    // Test to_string
    {
        ConnectionString conn;
        conn.protocol = Protocol::Shm;
        conn.buffer_name = "test";
        conn.buffer_size = 2048;
        conn.metadata_size = 1024;
        conn.mode = "oneway";
        
        auto str = conn.to_string();
        assert(str == "shm://test?buffer_size=2048&metadata_size=1024&mode=oneway");
        std::cout << "  ✓ to_string for SHM" << std::endl;
    }
    
    std::cout << "ConnectionString tests passed!" << std::endl;
}

void test_gst_caps() {
    std::cout << "\nTesting GstCaps..." << std::endl;
    
    // Test simple caps parsing
    {
        auto caps = GstCaps::parse("video/x-raw,format=RGB,width=640,height=480");
        assert(caps.width == 640);
        assert(caps.height == 480);
        assert(caps.format == "RGB");
        assert(!caps.framerate.has_value());
        std::cout << "  ✓ Parse simple caps" << std::endl;
    }
    
    // Test caps with framerate
    {
        auto caps = GstCaps::parse("video/x-raw,format=BGR,width=1920,height=1080,framerate=30/1");
        assert(caps.width == 1920);
        assert(caps.height == 1080);
        assert(caps.format == "BGR");
        assert(caps.framerate.has_value());
        assert(caps.framerate->first == 30);
        assert(caps.framerate->second == 1);
        std::cout << "  ✓ Parse caps with framerate" << std::endl;
    }
    
    // Test caps with type annotations (GStreamer format)
    {
        auto caps = GstCaps::parse("video/x-raw,format=(string)RGB,width=(int)640,height=(int)480,framerate=(fraction)30/1");
        assert(caps.width == 640);
        assert(caps.height == 480);
        assert(caps.format == "RGB");
        assert(caps.framerate.has_value());
        assert(caps.framerate->first == 30);
        std::cout << "  ✓ Parse caps with type annotations" << std::endl;
    }
    
    // Test from_simple
    {
        auto caps = GstCaps::from_simple(800, 600, "RGBA");
        assert(caps.width == 800);
        assert(caps.height == 600);
        assert(caps.format == "RGBA");
        std::cout << "  ✓ from_simple" << std::endl;
    }
    
    // Test get_channels
    {
        assert(GstCaps::from_simple(640, 480, "RGB").get_channels() == 3);
        assert(GstCaps::from_simple(640, 480, "RGBA").get_channels() == 4);
        assert(GstCaps::from_simple(640, 480, "GRAY8").get_channels() == 1);
        std::cout << "  ✓ get_channels" << std::endl;
    }
    
    // Test get_frame_size
    {
        assert(GstCaps::from_simple(640, 480, "RGB").get_frame_size() == 640 * 480 * 3);
        assert(GstCaps::from_simple(1920, 1080, "RGBA").get_frame_size() == 1920 * 1080 * 4);
        std::cout << "  ✓ get_frame_size" << std::endl;
    }
    
    // Test to_string
    {
        auto caps = GstCaps::from_simple(640, 480, "RGB");
        auto str = caps.to_string();
        assert(str == "video/x-raw,format=RGB,width=640,height=480");
        std::cout << "  ✓ to_string" << std::endl;
    }
    
    std::cout << "GstCaps tests passed!" << std::endl;
}

int main() {
    std::cout << "=== Rocket Welder SDK C++ Tests ===" << std::endl;
    
    try {
        test_connection_string();
        test_gst_caps();
        
        std::cout << "\n✅ All tests passed!" << std::endl;
        return 0;
    } catch (const std::exception& e) {
        std::cerr << "\n❌ Test failed: " << e.what() << std::endl;
        return 1;
    }
}