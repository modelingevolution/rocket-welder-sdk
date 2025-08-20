using System;
using Xunit;
using RocketWelder.SDK;

namespace RocketWelder.SDK.Tests
{
    public class ConnectionStringTests
    {
        [Theory]
        [InlineData("shm://myBuffer", Protocol.Shm, null, null, "myBuffer", null)]
        [InlineData("shm://myBuffer?size=1048576", Protocol.Shm, null, null, "myBuffer", 1048576)]
        [InlineData("mjpeg+http://192.168.1.100:8080", Protocol.Mjpeg | Protocol.Http, "192.168.1.100", 8080, null, null)]
        [InlineData("mjpeg+tcp://localhost:5000", Protocol.Mjpeg | Protocol.Tcp, "localhost", 5000, null, null)]
        public void Should_Parse_Connection_String_Correctly(string connectionString, Protocol expectedProtocol, 
            string expectedHost, int? expectedPort, string expectedPath, int? expectedSize)
        {
            // Act
            var parsed = ConnectionString.Parse(connectionString);
            
            // Assert
            Assert.Equal(expectedProtocol, parsed.Protocol);
            Assert.Equal(expectedHost, parsed.Host);
            Assert.Equal(expectedPort, parsed.Port);
            Assert.Equal(expectedPath ?? parsed.BufferName, parsed.Protocol == Protocol.ZeroBuffer ? parsed.BufferName : parsed.Path);
            if (expectedSize.HasValue)
            {
                Assert.Equal(expectedSize.Value, parsed.BufferSize);
            }
        }

        [Fact]
        public void Should_Parse_ZeroBuffer_With_Size()
        {
            // Arrange
            var connectionString = "zerobuffer://testBuffer?size=2097152";
            
            // Act
            var parsed = ConnectionString.Parse(connectionString);
            
            // Assert
            Assert.Equal(Protocol.ZeroBuffer, parsed.Protocol);
            Assert.Equal("testBuffer", parsed.BufferName);
            Assert.Equal(2097152, parsed.BufferSize);
        }

        [Fact]
        public void Should_Parse_ZeroBuffer_With_Size_And_Metadata()
        {
            // Arrange
            var connectionString = "zerobuffer://testBuffer?size=2097152&metadata=4096";
            
            // Act
            var parsed = ConnectionString.Parse(connectionString);
            
            // Assert
            Assert.Equal(Protocol.ZeroBuffer, parsed.Protocol);
            Assert.Equal("testBuffer", parsed.BufferName);
            Assert.Equal(2097152, parsed.BufferSize);
            Assert.Equal(4096, parsed.MetadataSize);
        }

        [Fact]
        public void Should_Use_Default_Buffer_Size_When_Not_Specified()
        {
            // Arrange
            var connectionString = "zerobuffer://myBuffer";
            
            // Act
            var parsed = ConnectionString.Parse(connectionString);
            
            // Assert
            Assert.Equal(Protocol.ZeroBuffer, parsed.Protocol);
            Assert.Equal("myBuffer", parsed.BufferName);
            Assert.Equal(256 * 1024 * 1024, parsed.BufferSize); // Default 256MB
        }

        [Fact]
        public void Should_Parse_Human_Readable_Sizes()
        {
            // Arrange & Act & Assert
            var conn1 = ConnectionString.Parse("zerobuffer://buffer1?size=1MB");
            Assert.Equal(1024 * 1024, conn1.BufferSize);
            
            var conn2 = ConnectionString.Parse("zerobuffer://buffer2?size=256MB");
            Assert.Equal(256 * 1024 * 1024, conn2.BufferSize);
            
            var conn3 = ConnectionString.Parse("zerobuffer://buffer3?size=1GB");
            Assert.Equal(1024 * 1024 * 1024, conn3.BufferSize);
        }

        [Fact]
        public void Should_Parse_Mjpeg_Http_Connection()
        {
            // Arrange
            var connectionString = "mjpeg+http://192.168.1.100:8080/video/stream";
            
            // Act
            var parsed = ConnectionString.Parse(connectionString);
            
            // Assert
            Assert.Equal(Protocol.MjpegHttp, parsed.Protocol);
            Assert.Equal("192.168.1.100", parsed.Host);
            Assert.Equal(8080, parsed.Port);
            Assert.Equal("video/stream", parsed.Path);
        }

        [Fact]
        public void Should_Parse_Mjpeg_Tcp_Connection()
        {
            // Arrange
            var connectionString = "mjpeg+tcp://camera.local:5000";
            
            // Act
            var parsed = ConnectionString.Parse(connectionString);
            
            // Assert
            Assert.Equal(Protocol.MjpegTcp, parsed.Protocol);
            Assert.Equal("camera.local", parsed.Host);
            Assert.Equal(5000, parsed.Port);
            Assert.Null(parsed.Path);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("invalid")]
        [InlineData("http://example.com")]
        [InlineData("unknown://test")]
        public void Should_Throw_On_Invalid_Connection_String(string connectionString)
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => ConnectionString.Parse(connectionString));
        }

        [Fact]
        public void Should_Support_Case_Insensitive_Protocol()
        {
            // Arrange & Act
            var conn1 = ConnectionString.Parse("ZEROBUFFER://buffer1");
            var conn2 = ConnectionString.Parse("ZeroBuffer://buffer2");
            var conn3 = ConnectionString.Parse("zerobuffer://buffer3");
            
            // Assert
            Assert.Equal(Protocol.ZeroBuffer, conn1.Protocol);
            Assert.Equal(Protocol.ZeroBuffer, conn2.Protocol);
            Assert.Equal(Protocol.ZeroBuffer, conn3.Protocol);
        }

        [Fact]
        public void Should_Parse_Query_Parameters()
        {
            // Arrange
            var connectionString = "zerobuffer://myBuffer?size=1MB&metadata=4KB&custom=value";
            
            // Act
            var parsed = ConnectionString.Parse(connectionString);
            
            // Assert
            Assert.Equal("myBuffer", parsed.BufferName);
            Assert.Equal(1024 * 1024, parsed.BufferSize);
            Assert.Equal(4 * 1024, parsed.MetadataSize);
        }

        [Fact]
        public void Should_ToString_Correctly()
        {
            // Arrange
            var conn = new ConnectionString
            {
                Protocol = Protocol.ZeroBuffer,
                BufferName = "testBuffer",
                BufferSize = 1024 * 1024,
                MetadataSize = 4096
            };
            
            // Act
            var str = conn.ToString();
            
            // Assert
            Assert.Contains("zerobuffer://testBuffer", str);
        }
    }
}