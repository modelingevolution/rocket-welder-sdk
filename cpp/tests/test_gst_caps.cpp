#include <gtest/gtest.h>
#include <rocket-welder/gst_caps.hpp>

using namespace rocket_welder;

TEST(GstCapsTest, ParseSimpleCaps) {
    auto caps = GstCaps::parse("video/x-raw,format=RGB,width=640,height=480");
    EXPECT_EQ(caps.width, 640);
    EXPECT_EQ(caps.height, 480);
    EXPECT_EQ(caps.format, "RGB");
    EXPECT_FALSE(caps.framerate.has_value());
}

TEST(GstCapsTest, ParseCapsWithFramerate) {
    auto caps = GstCaps::parse("video/x-raw,format=BGR,width=1920,height=1080,framerate=30/1");
    EXPECT_EQ(caps.width, 1920);
    EXPECT_EQ(caps.height, 1080);
    EXPECT_EQ(caps.format, "BGR");
    EXPECT_TRUE(caps.framerate.has_value());
    EXPECT_EQ(caps.framerate->first, 30);
    EXPECT_EQ(caps.framerate->second, 1);
}

TEST(GstCapsTest, ParseCapsWithTypeAnnotations) {
    auto caps = GstCaps::parse("video/x-raw,format=(string)RGB,width=(int)640,height=(int)480,framerate=(fraction)30/1");
    EXPECT_EQ(caps.width, 640);
    EXPECT_EQ(caps.height, 480);
    EXPECT_EQ(caps.format, "RGB");
    EXPECT_TRUE(caps.framerate.has_value());
    EXPECT_EQ(caps.framerate->first, 30);
    EXPECT_EQ(caps.framerate->second, 1);
}

TEST(GstCapsTest, ParseCapsWithoutPrefix) {
    auto caps = GstCaps::parse("format=GRAY8,width=320,height=240");
    EXPECT_EQ(caps.width, 320);
    EXPECT_EQ(caps.height, 240);
    EXPECT_EQ(caps.format, "GRAY8");
}

TEST(GstCapsTest, FromSimple) {
    auto caps = GstCaps::from_simple(800, 600, "RGBA");
    EXPECT_EQ(caps.width, 800);
    EXPECT_EQ(caps.height, 600);
    EXPECT_EQ(caps.format, "RGBA");
    EXPECT_FALSE(caps.framerate.has_value());
}

TEST(GstCapsTest, FromSimpleDefaultFormat) {
    auto caps = GstCaps::from_simple(1024, 768);
    EXPECT_EQ(caps.width, 1024);
    EXPECT_EQ(caps.height, 768);
    EXPECT_EQ(caps.format, "RGB");
}

TEST(GstCapsTest, TryParseValid) {
    auto result = GstCaps::try_parse("video/x-raw,format=RGB,width=640,height=480");
    EXPECT_TRUE(result.has_value());
    EXPECT_EQ(result->width, 640);
    EXPECT_EQ(result->height, 480);
}

TEST(GstCapsTest, TryParseInvalid) {
    auto result = GstCaps::try_parse("invalid caps string");
    EXPECT_FALSE(result.has_value());
}

TEST(GstCapsTest, GetChannelsRGB) {
    auto caps = GstCaps::from_simple(640, 480, "RGB");
    EXPECT_EQ(caps.get_channels(), 3);
}

TEST(GstCapsTest, GetChannelsBGR) {
    auto caps = GstCaps::from_simple(640, 480, "BGR");
    EXPECT_EQ(caps.get_channels(), 3);
}

TEST(GstCapsTest, GetChannelsRGBA) {
    auto caps = GstCaps::from_simple(640, 480, "RGBA");
    EXPECT_EQ(caps.get_channels(), 4);
}

TEST(GstCapsTest, GetChannelsGray) {
    auto caps = GstCaps::from_simple(640, 480, "GRAY8");
    EXPECT_EQ(caps.get_channels(), 1);
}

TEST(GstCapsTest, GetChannelsGray16) {
    auto caps = GstCaps::from_simple(640, 480, "GRAY16_LE");
    EXPECT_EQ(caps.get_channels(), 1);
}

TEST(GstCapsTest, GetBytesPerPixelRGB) {
    auto caps = GstCaps::from_simple(640, 480, "RGB");
    EXPECT_EQ(caps.get_bytes_per_pixel(), 3);
}

TEST(GstCapsTest, GetBytesPerPixelRGBA) {
    auto caps = GstCaps::from_simple(640, 480, "RGBA");
    EXPECT_EQ(caps.get_bytes_per_pixel(), 4);
}

TEST(GstCapsTest, GetBytesPerPixelGray8) {
    auto caps = GstCaps::from_simple(640, 480, "GRAY8");
    EXPECT_EQ(caps.get_bytes_per_pixel(), 1);
}

TEST(GstCapsTest, GetBytesPerPixelGray16) {
    auto caps = GstCaps::from_simple(640, 480, "GRAY16_LE");
    EXPECT_EQ(caps.get_bytes_per_pixel(), 2);
}

TEST(GstCapsTest, GetFrameSizeRGB) {
    auto caps = GstCaps::from_simple(640, 480, "RGB");
    EXPECT_EQ(caps.get_frame_size(), 640 * 480 * 3);
}

TEST(GstCapsTest, GetFrameSizeRGBA) {
    auto caps = GstCaps::from_simple(1920, 1080, "RGBA");
    EXPECT_EQ(caps.get_frame_size(), 1920 * 1080 * 4);
}

TEST(GstCapsTest, GetOpenCvTypeRGB) {
    auto caps = GstCaps::from_simple(640, 480, "RGB");
    EXPECT_EQ(caps.get_opencv_type(), CV_8UC3);
}

TEST(GstCapsTest, GetOpenCvTypeGray8) {
    auto caps = GstCaps::from_simple(640, 480, "GRAY8");
    EXPECT_EQ(caps.get_opencv_type(), CV_8UC1);
}

TEST(GstCapsTest, GetOpenCvTypeGray16) {
    auto caps = GstCaps::from_simple(640, 480, "GRAY16_LE");
    EXPECT_EQ(caps.get_opencv_type(), CV_16UC1);
}

TEST(GstCapsTest, ToStringSimple) {
    auto caps = GstCaps::from_simple(640, 480, "RGB");
    auto str = caps.to_string();
    EXPECT_EQ(str, "video/x-raw,format=RGB,width=640,height=480");
}

TEST(GstCapsTest, ToStringWithFramerate) {
    GstCaps caps;
    caps.width = 1920;
    caps.height = 1080;
    caps.format = "BGR";
    caps.framerate = std::make_pair(60, 1);
    
    auto str = caps.to_string();
    EXPECT_EQ(str, "video/x-raw,format=BGR,width=1920,height=1080,framerate=60/1");
}

TEST(GstCapsTest, EmptyStringThrows) {
    EXPECT_THROW(GstCaps::parse(""), std::invalid_argument);
}

TEST(GstCapsTest, MissingWidthThrows) {
    EXPECT_THROW(GstCaps::parse("video/x-raw,format=RGB,height=480"), std::invalid_argument);
}

TEST(GstCapsTest, MissingHeightThrows) {
    EXPECT_THROW(GstCaps::parse("video/x-raw,format=RGB,width=640"), std::invalid_argument);
}

TEST(GstCapsTest, CreateMatZeroCopy) {
    auto caps = GstCaps::from_simple(2, 2, "RGB");
    uint8_t data[12] = {0};  // 2x2x3 bytes
    
    auto mat = caps.create_mat(data);
    EXPECT_EQ(mat.rows, 2);
    EXPECT_EQ(mat.cols, 2);
    EXPECT_EQ(mat.channels(), 3);
    EXPECT_EQ(mat.data, data);  // Should point to same memory (zero-copy)
}

TEST(GstCapsTest, CreateMatFromBuffer) {
    auto caps = GstCaps::from_simple(2, 2, "GRAY8");
    uint8_t buffer[4] = {1, 2, 3, 4};  // 2x2x1 bytes
    
    auto mat = caps.create_mat_from_buffer(buffer, 4);
    EXPECT_EQ(mat.rows, 2);
    EXPECT_EQ(mat.cols, 2);
    EXPECT_EQ(mat.channels(), 1);
    EXPECT_EQ(mat.data, buffer);  // Should point to same memory
}