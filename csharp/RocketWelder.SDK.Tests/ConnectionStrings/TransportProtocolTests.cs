using RocketWelder.SDK;
using Xunit;

namespace RocketWelder.SDK.Tests.HighLevel;

public class TransportProtocolTests
{
    #region TryParse tests

    [Theory]
    [InlineData("file", TransportKind.File)]
    [InlineData("FILE", TransportKind.File)]
    [InlineData("File", TransportKind.File)]
    [InlineData("socket", TransportKind.Socket)]
    [InlineData("SOCKET", TransportKind.Socket)]
    [InlineData("nng+push+ipc", TransportKind.NngPushIpc)]
    [InlineData("NNG+PUSH+IPC", TransportKind.NngPushIpc)]
    [InlineData("nng+push+tcp", TransportKind.NngPushTcp)]
    [InlineData("nng+pull+ipc", TransportKind.NngPullIpc)]
    [InlineData("nng+pull+tcp", TransportKind.NngPullTcp)]
    [InlineData("nng+pub+ipc", TransportKind.NngPubIpc)]
    [InlineData("nng+pub+tcp", TransportKind.NngPubTcp)]
    [InlineData("nng+sub+ipc", TransportKind.NngSubIpc)]
    [InlineData("nng+sub+tcp", TransportKind.NngSubTcp)]
    public void TryParse_ValidSchema_ReturnsCorrectKind(string schema, TransportKind expectedKind)
    {
        var success = TransportProtocol.TryParse(schema, out var result);

        Assert.True(success);
        Assert.Equal(expectedKind, result.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("unknown")]
    [InlineData("nng+push")]
    [InlineData("nng")]
    [InlineData("tcp")]
    public void TryParse_InvalidSchema_ReturnsFalse(string? schema)
    {
        var success = TransportProtocol.TryParse(schema, out _);

        Assert.False(success);
    }

    #endregion

    #region Classification properties

    [Theory]
    [InlineData(TransportKind.File, true, false, false)]
    [InlineData(TransportKind.Socket, false, true, false)]
    [InlineData(TransportKind.NngPushIpc, false, false, true)]
    [InlineData(TransportKind.NngPushTcp, false, false, true)]
    [InlineData(TransportKind.NngPubIpc, false, false, true)]
    public void Classification_Properties_AreCorrect(
        TransportKind kind, bool isFile, bool isSocket, bool isNng)
    {
        var protocol = kind switch
        {
            TransportKind.File => TransportProtocol.File,
            TransportKind.Socket => TransportProtocol.Socket,
            TransportKind.NngPushIpc => TransportProtocol.NngPushIpc,
            TransportKind.NngPushTcp => TransportProtocol.NngPushTcp,
            TransportKind.NngPubIpc => TransportProtocol.NngPubIpc,
            _ => default
        };

        Assert.Equal(isFile, protocol.IsFile);
        Assert.Equal(isSocket, protocol.IsSocket);
        Assert.Equal(isNng, protocol.IsNng);
    }

    [Fact]
    public void IsPush_IsCorrectForPushProtocols()
    {
        Assert.True(TransportProtocol.NngPushIpc.IsPush);
        Assert.True(TransportProtocol.NngPushTcp.IsPush);
        Assert.False(TransportProtocol.NngPubIpc.IsPush);
        Assert.False(TransportProtocol.NngPullIpc.IsPush);
    }

    [Fact]
    public void IsPub_IsCorrectForPubProtocols()
    {
        Assert.True(TransportProtocol.NngPubIpc.IsPub);
        Assert.True(TransportProtocol.NngPubTcp.IsPub);
        Assert.False(TransportProtocol.NngPushIpc.IsPub);
        Assert.False(TransportProtocol.NngSubIpc.IsPub);
    }

    #endregion

    #region CreateNngAddress tests

    [Theory]
    [InlineData("tmp/keypoints", "ipc:///tmp/keypoints")]
    [InlineData("/tmp/keypoints", "ipc:///tmp/keypoints")]
    public void CreateNngAddress_IpcProtocol_CreatesCorrectAddress(string path, string expected)
    {
        var address = TransportProtocol.NngPushIpc.CreateNngAddress(path);

        Assert.Equal(expected, address);
    }

    [Theory]
    [InlineData("localhost:5555", "tcp://localhost:5555")]
    [InlineData("192.168.1.100:8080", "tcp://192.168.1.100:8080")]
    public void CreateNngAddress_TcpProtocol_CreatesCorrectAddress(string hostPort, string expected)
    {
        var address = TransportProtocol.NngPushTcp.CreateNngAddress(hostPort);

        Assert.Equal(expected, address);
    }

    [Fact]
    public void CreateNngAddress_NonNngProtocol_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TransportProtocol.File.CreateNngAddress("/path"));

        Assert.Throws<InvalidOperationException>(() =>
            TransportProtocol.Socket.CreateNngAddress("/path"));
    }

    #endregion

    #region ToString tests

    [Fact]
    public void ToString_ReturnsSchema()
    {
        Assert.Equal("file", TransportProtocol.File.ToString());
        Assert.Equal("socket", TransportProtocol.Socket.ToString());
        Assert.Equal("nng+push+ipc", TransportProtocol.NngPushIpc.ToString());
    }

    #endregion
}
