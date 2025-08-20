using System.Text.Json;
using Xunit;
using RocketWelder.SDK;

namespace RocketWelder.SDK.Tests
{
    public class GstMetadataTests
    {
        [Fact]
        public void Should_Deserialize_GstMetadata_From_Cpp_Json()
        {
            // Arrange - JSON exactly as written by C++ gstzerofilter
            var json = @"{
                ""type"": ""gstreamer-filter"",
                ""version"": ""GStreamer 1.20.3"",
                ""caps"": ""video/x-raw, format=(string)RGB, width=(int)640, height=(int)480, framerate=(fraction)30/1, multiview-mode=(string)mono, pixel-aspect-ratio=(fraction)1/1, interlace-mode=(string)progressive"",
                ""element_name"": ""zerofilter0""
            }";

            // Act
            var metadata = JsonSerializer.Deserialize<GstMetadata>(json);

            // Assert
            Assert.NotNull(metadata);
            Assert.Equal("gstreamer-filter", metadata.Type);
            Assert.Equal("GStreamer 1.20.3", metadata.Version);
            Assert.Equal("zerofilter0", metadata.ElementName);
            
            // Verify GstCaps was properly deserialized
            Assert.Equal(640, metadata.Caps.Width);
            Assert.Equal(480, metadata.Caps.Height);
            Assert.Equal("RGB", metadata.Caps.Format);
            Assert.Equal(30, metadata.Caps.FramerateNum);
            Assert.Equal(1, metadata.Caps.FramerateDen);
            Assert.Equal(30.0, metadata.Caps.FrameRate);
        }

        [Fact]
        public void Should_Serialize_GstMetadata_To_Json()
        {
            // Arrange
            var caps = GstCaps.Parse("video/x-raw, format=(string)BGR, width=(int)1920, height=(int)1080, framerate=(fraction)60/1", null);
            var metadata = new GstMetadata(
                Type: "gstreamer-filter",
                Version: "GStreamer 1.20.3",
                Caps: caps,
                ElementName: "myfilter0"
            );

            // Act
            var json = JsonSerializer.Serialize(metadata);
            var deserialized = JsonSerializer.Deserialize<GstMetadata>(json);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(metadata.Type, deserialized.Type);
            Assert.Equal(metadata.Version, deserialized.Version);
            Assert.Equal(metadata.ElementName, deserialized.ElementName);
            Assert.Equal(metadata.Caps.Width, deserialized.Caps.Width);
            Assert.Equal(metadata.Caps.Height, deserialized.Caps.Height);
            Assert.Equal(metadata.Caps.Format, deserialized.Caps.Format);
        }

        [Fact]
        public void Should_Handle_Complex_Caps_String()
        {
            // Arrange - Complex caps with many properties
            var json = @"{
                ""type"": ""gstreamer-filter"",
                ""version"": ""GStreamer 1.22.0"",
                ""caps"": ""video/x-raw, format=(string)RGBA, width=(int)1280, height=(int)720, framerate=(fraction)25/1, multiview-mode=(string)mono, pixel-aspect-ratio=(fraction)1/1, interlace-mode=(string)progressive, colorimetry=(string)bt709"",
                ""element_name"": ""videofilter0""
            }";

            // Act
            var metadata = JsonSerializer.Deserialize<GstMetadata>(json);

            // Assert
            Assert.NotNull(metadata);
            Assert.Equal(1280, metadata.Caps.Width);
            Assert.Equal(720, metadata.Caps.Height);
            Assert.Equal("RGBA", metadata.Caps.Format);
            Assert.Equal(25, metadata.Caps.FramerateNum);
            Assert.Equal(1, metadata.Caps.FramerateDen);
            Assert.Equal(4, metadata.Caps.BytesPerPixel); // RGBA = 4 bytes
            Assert.Equal(4, metadata.Caps.Channels); // RGBA = 4 channels
        }

        [Fact]
        public void Should_Handle_Minimal_Caps_String()
        {
            // Arrange - Minimal caps without framerate
            var json = @"{
                ""type"": ""gstreamer-filter"",
                ""version"": ""GStreamer 1.20.3"",
                ""caps"": ""video/x-raw, format=(string)GRAY8, width=(int)320, height=(int)240"",
                ""element_name"": ""grayfilter0""
            }";

            // Act
            var metadata = JsonSerializer.Deserialize<GstMetadata>(json);

            // Assert
            Assert.NotNull(metadata);
            Assert.Equal(320, metadata.Caps.Width);
            Assert.Equal(240, metadata.Caps.Height);
            Assert.Equal("GRAY8", metadata.Caps.Format);
            Assert.Null(metadata.Caps.FramerateNum);
            Assert.Null(metadata.Caps.FramerateDen);
            Assert.Null(metadata.Caps.FrameRate);
            Assert.Equal(1, metadata.Caps.BytesPerPixel); // GRAY8 = 1 byte
            Assert.Equal(1, metadata.Caps.Channels); // GRAY8 = 1 channel
        }

        [Fact]
        public void Should_Roundtrip_Serialize_Deserialize()
        {
            // Arrange - Use a simpler caps string that will roundtrip properly
            var capsString = "video/x-raw, format=(string)YUY2, width=(int)720, height=(int)576, framerate=(fraction)25/1";
            var originalCaps = GstCaps.Parse(capsString, null);
            var original = new GstMetadata(
                Type: "gstreamer-filter",
                Version: "GStreamer 1.20.3",
                Caps: originalCaps,
                ElementName: "testfilter0"
            );

            // Act - Serialize and deserialize
            var json = JsonSerializer.Serialize(original);
            var roundtripped = JsonSerializer.Deserialize<GstMetadata>(json);

            // Assert - All properties should match
            Assert.NotNull(roundtripped);
            Assert.Equal(original.Type, roundtripped.Type);
            Assert.Equal(original.Version, roundtripped.Version);
            Assert.Equal(original.ElementName, roundtripped.ElementName);
            
            // Check caps details
            Assert.Equal(original.Caps.Width, roundtripped.Caps.Width);
            Assert.Equal(original.Caps.Height, roundtripped.Caps.Height);
            Assert.Equal(original.Caps.Format, roundtripped.Caps.Format);
            Assert.Equal(original.Caps.FramerateNum, roundtripped.Caps.FramerateNum);
            Assert.Equal(original.Caps.FramerateDen, roundtripped.Caps.FramerateDen);
            Assert.Equal(original.Caps.BytesPerPixel, roundtripped.Caps.BytesPerPixel);
            Assert.Equal(original.Caps.Channels, roundtripped.Caps.Channels);
        }

        [Fact]
        public void Should_Calculate_Frame_Properties_Correctly()
        {
            // Arrange
            var json = @"{
                ""type"": ""gstreamer-filter"",
                ""version"": ""GStreamer 1.20.3"",
                ""caps"": ""video/x-raw, format=(string)RGB, width=(int)1920, height=(int)1080, framerate=(fraction)30/1"",
                ""element_name"": ""hdfilter0""
            }";

            // Act
            var metadata = JsonSerializer.Deserialize<GstMetadata>(json);

            // Assert
            Assert.NotNull(metadata);
            Assert.Equal(1920 * 1080 * 3, metadata.Caps.FrameSize); // RGB = 3 bytes per pixel
            Assert.Equal(30.0, metadata.Caps.FrameRate);
        }
    }
}