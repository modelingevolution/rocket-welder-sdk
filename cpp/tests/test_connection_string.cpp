#include <gtest/gtest.h>
#include <rocket-welder/connection_string.hpp>

using namespace rocket_welder;

TEST(ConnectionStringTest, ParseShmProtocol) {
    auto conn = ConnectionString::parse("shm://mybuffer");
    EXPECT_EQ(conn.protocol, Protocol::Shm);
    EXPECT_EQ(conn.buffer_name.value_or(""), "mybuffer");
}

TEST(ConnectionStringTest, ParseShmWithDefaults) {
    auto conn = ConnectionString::parse("shm://");
    EXPECT_EQ(conn.protocol, Protocol::Shm);
    EXPECT_EQ(conn.buffer_name.value_or(""), "default");
}

TEST(ConnectionStringTest, ParseShmWithQuery) {
    auto conn = ConnectionString::parse("shm://test?buffer_size=1024&metadata_size=512&mode=duplex");
    EXPECT_EQ(conn.protocol, Protocol::Shm);
    EXPECT_EQ(conn.buffer_name.value_or(""), "test");
    EXPECT_EQ(conn.buffer_size, 1024);
    EXPECT_EQ(conn.metadata_size, 512);
    EXPECT_EQ(conn.mode, "duplex");
}

TEST(ConnectionStringTest, ParseMjpegHttp) {
    auto conn = ConnectionString::parse("mjpeg+http://localhost:8080/stream");
    EXPECT_EQ(conn.protocol, Protocol::Mjpeg | Protocol::Http);
    EXPECT_EQ(conn.host.value_or(""), "localhost");
    EXPECT_EQ(conn.port.value_or(0), 8080);
    EXPECT_EQ(conn.path.value_or(""), "stream");
}

TEST(ConnectionStringTest, ParseMjpegTcp) {
    auto conn = ConnectionString::parse("mjpeg+tcp://192.168.1.100:5000");
    EXPECT_EQ(conn.protocol, Protocol::Mjpeg | Protocol::Tcp);
    EXPECT_EQ(conn.host.value_or(""), "192.168.1.100");
    EXPECT_EQ(conn.port.value_or(0), 5000);
}

TEST(ConnectionStringTest, TryParseValid) {
    auto result = ConnectionString::try_parse("shm://buffer");
    EXPECT_TRUE(result.has_value());
    EXPECT_EQ(result->protocol, Protocol::Shm);
    EXPECT_EQ(result->buffer_name.value_or(""), "buffer");
}

TEST(ConnectionStringTest, TryParseInvalid) {
    auto result = ConnectionString::try_parse("invalid://protocol");
    EXPECT_FALSE(result.has_value());
}

TEST(ConnectionStringTest, EmptyStringThrows) {
    EXPECT_THROW(ConnectionString::parse(""), std::invalid_argument);
}

TEST(ConnectionStringTest, ToStringShm) {
    ConnectionString conn;
    conn.protocol = Protocol::Shm;
    conn.buffer_name = "test";
    conn.buffer_size = 2048;
    conn.metadata_size = 1024;
    conn.mode = "oneway";
    
    auto str = conn.to_string();
    EXPECT_EQ(str, "shm://test?buffer_size=2048&metadata_size=1024&mode=oneway");
}

TEST(ConnectionStringTest, ToStringMjpegHttp) {
    ConnectionString conn;
    conn.protocol = Protocol::Mjpeg | Protocol::Http;
    conn.host = "example.com";
    conn.port = 8080;
    conn.path = "video";
    
    auto str = conn.to_string();
    EXPECT_EQ(str, "mjpeg+http://example.com:8080/video");
}

TEST(ConnectionStringTest, ProtocolPlusOperator) {
    Protocol combined = Protocol::Mjpeg + Protocol::Http;
    EXPECT_EQ(combined, Protocol::Mjpeg | Protocol::Http);
}

TEST(ConnectionStringTest, HasFlag) {
    Protocol proto = Protocol::Mjpeg | Protocol::Http;
    EXPECT_TRUE(has_flag(proto, Protocol::Mjpeg));
    EXPECT_TRUE(has_flag(proto, Protocol::Http));
    EXPECT_FALSE(has_flag(proto, Protocol::Tcp));
}