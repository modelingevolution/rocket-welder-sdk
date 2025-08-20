using System;
using Xunit;
using RocketWelder.SDK;

namespace RocketWelder.SDK.Tests
{
    public class ConnectionStringTests
    {
        [Theory]
        [InlineData("shm://myBuffer", Protocol.Shm, null, null, "myBuffer")]
        [InlineData("shm://myBuffer?size=1MB", Protocol.Shm, null, null, "myBuffer")]
        [InlineData("mjpeg+http://192.168.1.100:8080", Protocol.Mjpeg | Protocol.Http, "192.168.1.100", 8080, null)]
        [InlineData("mjpeg+tcp://localhost:5000", Protocol.Mjpeg | Protocol.Tcp, "localhost", 5000, null)]
        public void Should_Parse_Connection_String_Correctly(string connectionString, Protocol expectedProtocol, 
            string expectedHost, int? expectedPort, string expectedBufferName)
        {
            // Act
            var parsed = ConnectionString.Parse(connectionString);
            
            // Assert
            Assert.Equal(expectedProtocol, parsed.Protocol);
            Assert.Equal(expectedHost, parsed.Host);
            Assert.Equal(expectedPort, parsed.Port);
            Assert.Equal(expectedBufferName, parsed.BufferName);
        }

        [Fact]
        public void Should_Parse_Shm_With_Size()
        {
            // Arrange
            var connectionString = "shm://testBuffer?size=2MB";
            
            // Act
            var parsed = ConnectionString.Parse(connectionString);
            
            // Assert
            Assert.Equal(Protocol.Shm, parsed.Protocol);
            Assert.Equal("testBuffer", parsed.BufferName);
            Assert.Equal((Bytes)"2MB", parsed.BufferSize);
        }

        [Fact]
        public void Should_Parse_Shm_With_Size_And_Metadata()
        {
            // Arrange
            var connectionString = "shm://testBuffer?size=2MB&metadata=4KB";
            
            // Act
            var parsed = ConnectionString.Parse(connectionString);
            
            // Assert
            Assert.Equal(Protocol.Shm, parsed.Protocol);
            Assert.Equal("testBuffer", parsed.BufferName);
            Assert.Equal((Bytes)"2MB", parsed.BufferSize);
            Assert.Equal((Bytes)"4KB", parsed.MetadataSize);
        }

        [Fact]
        public void Should_Use_Default_Buffer_Size_When_Not_Specified()
        {
            // Arrange
            var connectionString = "shm://myBuffer";
            
            // Act
            var parsed = ConnectionString.Parse(connectionString);
            
            // Assert
            Assert.Equal(Protocol.Shm, parsed.Protocol);
            Assert.Equal("myBuffer", parsed.BufferName);
            Assert.Equal((Bytes)"256MB", parsed.BufferSize); // Default 256MB
        }

        [Fact]
        public void Should_Parse_Human_Readable_Sizes()
        {
            // Arrange & Act & Assert
            var conn1 = ConnectionString.Parse("shm://buffer1?size=1MB");
            Assert.Equal((Bytes)"1MB", conn1.BufferSize);
            
            var conn2 = ConnectionString.Parse("shm://buffer2?size=256MB");
            Assert.Equal((Bytes)"256MB", conn2.BufferSize);
            
            var conn3 = ConnectionString.Parse("shm://buffer3?size=1GB");
            Assert.Equal((Bytes)"1GB", conn3.BufferSize);
        }

        [Fact]
        public void Should_Parse_Mjpeg_Http_Connection()
        {
            // Arrange
            var connectionString = "mjpeg+http://192.168.1.100:8080";
            
            // Act
            var parsed = ConnectionString.Parse(connectionString);
            
            // Assert
            Assert.Equal(Protocol.Mjpeg | Protocol.Http, parsed.Protocol);
            Assert.Equal("192.168.1.100", parsed.Host);
            Assert.Equal(8080, parsed.Port);
        }

        [Fact]
        public void Should_Parse_Mjpeg_Tcp_Connection()
        {
            // Arrange
            var connectionString = "mjpeg+tcp://camera.local:5000";
            
            // Act
            var parsed = ConnectionString.Parse(connectionString);
            
            // Assert
            Assert.Equal(Protocol.Mjpeg | Protocol.Tcp, parsed.Protocol);
            Assert.Equal("camera.local", parsed.Host);
            Assert.Equal(5000, parsed.Port);
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
            Assert.Throws<FormatException>(() => ConnectionString.Parse(connectionString));
        }

        [Fact]
        public void Should_Support_Case_Insensitive_Protocol()
        {
            // Arrange & Act
            var conn1 = ConnectionString.Parse("SHM://buffer1");
            var conn2 = ConnectionString.Parse("Shm://buffer2");
            var conn3 = ConnectionString.Parse("shm://buffer3");
            
            // Assert
            Assert.Equal(Protocol.Shm, conn1.Protocol);
            Assert.Equal(Protocol.Shm, conn2.Protocol);
            Assert.Equal(Protocol.Shm, conn3.Protocol);
        }

        [Fact]
        public void Should_Parse_Query_Parameters()
        {
            // Arrange
            var connectionString = "shm://myBuffer?size=1MB&metadata=4KB&custom=value";
            
            // Act
            var parsed = ConnectionString.Parse(connectionString);
            
            // Assert
            Assert.Equal("myBuffer", parsed.BufferName);
            Assert.Equal((Bytes)"1MB", parsed.BufferSize);
            Assert.Equal((Bytes)"4KB", parsed.MetadataSize);
        }

        [Fact]
        public void Should_ToString_Correctly()
        {
            // Arrange
            // ConnectionString is readonly struct, test ToString from Parse
            var conn = ConnectionString.Parse("shm://testBuffer?size=1MB&metadata=4KB");
            
            // Act
            var str = conn.ToString();
            
            // Assert
            Assert.Contains("shm://testBuffer", str);
            Assert.Contains("size=1MB", str);
            Assert.Contains("metadata=4KB", str);
        }
    }
}