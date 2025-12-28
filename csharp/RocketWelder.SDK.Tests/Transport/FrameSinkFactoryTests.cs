using System.Net.Sockets;
using RocketWelder.SDK;
using RocketWelder.SDK.Transport;
using Xunit;

namespace RocketWelder.SDK.Tests.Transport;

public class FrameSinkFactoryTests
{
    #region Create tests - Socket protocol

    [Fact]
    public async Task Create_SocketProtocol_BindsAsServer_AndAcceptsClient()
    {
        // FrameSinkFactory.Create with socket protocol should:
        // 1. Bind to socket path (be the SERVER)
        // 2. Wait for client to connect
        // 3. Return sink that writes to connected client
        //
        // This is the production flow:
        // - SDK container calls FrameSinkFactory.Create() → binds as server
        // - rocket-welder2 connects as client → reads frames

        var socketPath = $"/tmp/test-factory-server-{Guid.NewGuid()}.sock";
        var testData = new byte[] { 1, 2, 3, 4, 5 };
        byte[]? receivedData = null;

        try
        {
            // Producer (SDK) - factory creates server, waits for client
            var serverTask = Task.Run(() =>
            {
                using var sink = FrameSinkFactory.Create(TransportProtocol.Socket, socketPath);
                Assert.IsType<UnixSocketFrameSink>(sink);
                sink.WriteFrame(testData);
            });

            // Give server time to start listening
            await Task.Delay(100);

            // Consumer (rocket-welder2) - connects and reads
            using var source = await UnixSocketFrameSource.ConnectAsync(
                socketPath,
                timeout: TimeSpan.FromSeconds(5),
                retry: true);

            var frame = await source.ReadFrameAsync();
            receivedData = frame.ToArray();

            await serverTask;

            Assert.Equal(testData, receivedData);
        }
        finally
        {
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
    public void Integration_KeypointsConnectionString_ToFrameSink_Socket()
    {
        var cs = KeypointsConnectionString.Parse("socket:///tmp/kp-test.sock", null);

        Assert.Equal(TransportKind.Socket, cs.Protocol.Kind);
        Assert.Equal("/tmp/kp-test.sock", cs.Address);
    }

    #endregion
}
