using System.Net.Sockets;
using RocketWelder.SDK;
using RocketWelder.SDK.Transport;
using Xunit;

namespace RocketWelder.SDK.Tests.Transport;

public class FrameSinkFactoryTests
{
    #region Create tests - Socket protocol

    [Fact]
    public void Create_SocketProtocol_AttemptsUnixSocketConnection()
    {
        // Socket protocol should attempt Unix socket connection
        // This will throw SocketException because socket doesn't exist
        var protocol = TransportProtocol.Socket;
        var address = "/tmp/nonexistent-test-socket.sock";

        var ex = Assert.Throws<SocketException>(() => FrameSinkFactory.Create(protocol, address));

        // SocketException means it correctly tried to connect via Unix socket
        // Common errors: AddressNotAvailable, ConnectionRefused, or native errno for missing file
        Assert.True(ex.SocketErrorCode == SocketError.AddressNotAvailable
                 || ex.SocketErrorCode == SocketError.ConnectionRefused
                 || (int)ex.SocketErrorCode == 2);  // ENOENT - file not found on Linux
    }

    [Fact]
    public void Create_SocketProtocol_ReturnsUnixSocketFrameSink_WhenSocketExists()
    {
        // Create a real Unix socket server to test connection
        var socketPath = $"/tmp/test-sink-{Guid.NewGuid()}.sock";

        try
        {
            // Create listening socket
            using var server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            server.Bind(new UnixDomainSocketEndPoint(socketPath));
            server.Listen(1);

            // Create sink via factory
            using var sink = FrameSinkFactory.Create(TransportProtocol.Socket, socketPath);

            // Verify correct type
            Assert.IsType<UnixSocketFrameSink>(sink);
        }
        finally
        {
            // Cleanup
            if (File.Exists(socketPath))
                File.Delete(socketPath);
        }
    }

    #endregion

    #region Create tests - NNG protocols

    [Fact]
    public void Create_NngPubIpc_ReturnsNngFrameSink()
    {
        // NNG Pub sockets can bind without a listener
        var address = "ipc:///tmp/test-pub-sink";

        using var sink = FrameSinkFactory.Create(TransportProtocol.NngPubIpc, address);

        Assert.IsType<NngFrameSink>(sink);
    }

    [Fact]
    public void Create_NngPushIpc_ReturnsNngFrameSink()
    {
        // NNG Push sockets can bind without a listener
        var address = "ipc:///tmp/test-push-sink";

        using var sink = FrameSinkFactory.Create(TransportProtocol.NngPushIpc, address);

        Assert.IsType<NngFrameSink>(sink);
    }

    [Fact]
    public void Create_NngPubTcp_ReturnsNngFrameSink()
    {
        var address = "tcp://127.0.0.1:15555";

        using var sink = FrameSinkFactory.Create(TransportProtocol.NngPubTcp, address);

        Assert.IsType<NngFrameSink>(sink);
    }

    [Fact]
    public void Create_NngPushTcp_ReturnsNngFrameSink()
    {
        var address = "tcp://127.0.0.1:15556";

        using var sink = FrameSinkFactory.Create(TransportProtocol.NngPushTcp, address);

        Assert.IsType<NngFrameSink>(sink);
    }

    #endregion

    #region Create tests - File protocol

    [Fact]
    public void Create_FileProtocol_ReturnsStreamFrameSink()
    {
        var filePath = $"/tmp/test-sink-{Guid.NewGuid()}.bin";

        try
        {
            using var sink = FrameSinkFactory.Create(TransportProtocol.File, filePath);

            Assert.IsType<StreamFrameSink>(sink);
            Assert.True(File.Exists(filePath));
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Fact]
    public void Create_FileProtocol_CanWriteData()
    {
        var filePath = $"/tmp/test-sink-write-{Guid.NewGuid()}.bin";
        var testData = new byte[] { 1, 2, 3, 4, 5 };

        try
        {
            using (var sink = FrameSinkFactory.Create(TransportProtocol.File, filePath))
            {
                sink.WriteFrame(testData);
                sink.Flush();
            }

            // Verify file was written (with varint length prefix)
            Assert.True(File.Exists(filePath));
            var fileContent = File.ReadAllBytes(filePath);
            Assert.True(fileContent.Length > testData.Length); // Has length prefix
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Fact]
    public void Integration_SegmentationConnectionString_ToFrameSink_File()
    {
        var filePath = $"/tmp/test-seg-file-{Guid.NewGuid()}.bin";

        try
        {
            var cs = SegmentationConnectionString.Parse($"file://{filePath}", null);

            Assert.Equal(TransportKind.File, cs.Protocol.Kind);
            Assert.Equal(filePath, cs.Address);

            using var sink = FrameSinkFactory.Create(cs.Protocol, cs.Address);
            Assert.IsType<StreamFrameSink>(sink);
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    #endregion

    #region Create tests - NullFrameSink

    [Fact]
    public void Create_DefaultProtocol_ReturnsNullFrameSink()
    {
        // Default protocol (no URL specified) should return NullFrameSink
        var protocol = default(TransportProtocol);

        var sink = FrameSinkFactory.Create(protocol, "");

        Assert.IsType<NullFrameSink>(sink);
        Assert.Same(NullFrameSink.Instance, sink);
    }

    [Fact]
    public void CreateNull_ReturnsNullFrameSink()
    {
        var sink = FrameSinkFactory.CreateNull();

        Assert.IsType<NullFrameSink>(sink);
        Assert.Same(NullFrameSink.Instance, sink);
    }

    [Fact]
    public void NullFrameSink_IsSingleton()
    {
        var sink1 = NullFrameSink.Instance;
        var sink2 = NullFrameSink.Instance;

        Assert.Same(sink1, sink2);
    }

    [Fact]
    public void NullFrameSink_WriteFrame_DoesNotThrow()
    {
        var sink = NullFrameSink.Instance;
        var data = new byte[] { 1, 2, 3 };

        // Should not throw
        sink.WriteFrame(data);
    }

    [Fact]
    public async Task NullFrameSink_WriteFrameAsync_DoesNotThrow()
    {
        var sink = NullFrameSink.Instance;
        var data = new byte[] { 1, 2, 3 };

        // Should not throw
        await sink.WriteFrameAsync(data);
    }

    [Fact]
    public void NullFrameSink_Dispose_DoesNotThrow()
    {
        var sink = NullFrameSink.Instance;

        // Should not throw - singleton is never disposed
        sink.Dispose();
        sink.Dispose(); // Multiple calls should be safe
    }

    #endregion

    #region Create tests - error cases

    [Fact]
    public void Create_NngSubProtocol_ThrowsNotSupportedException()
    {
        // Sub is for receiving, not sinking
        Assert.Throws<NotSupportedException>(() =>
            FrameSinkFactory.Create(TransportProtocol.NngSubIpc, "ipc:///tmp/test"));
    }

    [Fact]
    public void Create_NngPullProtocol_ThrowsNotSupportedException()
    {
        // Pull is for receiving, not sinking
        Assert.Throws<NotSupportedException>(() =>
            FrameSinkFactory.Create(TransportProtocol.NngPullIpc, "ipc:///tmp/test"));
    }

    #endregion

    #region Integration tests - ConnectionString → FrameSinkFactory

    [Fact]
    public void Integration_SegmentationConnectionString_ToFrameSink_Socket()
    {
        // Parse URL via connection string, then create sink
        var cs = SegmentationConnectionString.Parse("socket:///tmp/test-integration.sock", null);

        Assert.Equal(TransportKind.Socket, cs.Protocol.Kind);
        Assert.Equal("/tmp/test-integration.sock", cs.Address);

        // Creating sink will fail (socket doesn't exist) but with correct exception type
        var ex = Assert.Throws<SocketException>(() =>
            FrameSinkFactory.Create(cs.Protocol, cs.Address));

        Assert.True(ex.SocketErrorCode == SocketError.AddressNotAvailable
                 || ex.SocketErrorCode == SocketError.ConnectionRefused
                 || (int)ex.SocketErrorCode == 2);
    }

    [Fact]
    public void Integration_SegmentationConnectionString_ToFrameSink_NngPubIpc()
    {
        var cs = SegmentationConnectionString.Parse("nng+pub+ipc://tmp/test-integration", null);

        Assert.Equal(TransportKind.NngPubIpc, cs.Protocol.Kind);
        Assert.Equal("ipc:///tmp/test-integration", cs.Address);

        using var sink = FrameSinkFactory.Create(cs.Protocol, cs.Address);
        Assert.IsType<NngFrameSink>(sink);
    }

    [Fact]
    public void Integration_KeyPointsConnectionString_ToFrameSink_Socket()
    {
        var cs = KeyPointsConnectionString.Parse("socket:///tmp/kp-test.sock", null);

        Assert.Equal(TransportKind.Socket, cs.Protocol.Kind);
        Assert.Equal("/tmp/kp-test.sock", cs.Address);
    }

    #endregion
}
