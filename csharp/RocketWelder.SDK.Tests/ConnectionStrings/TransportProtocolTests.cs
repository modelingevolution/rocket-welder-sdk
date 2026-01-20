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
    [InlineData("Socket", TransportKind.Socket)]
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
    [InlineData("tcp")]
    [InlineData("http")]
    public void TryParse_InvalidSchema_ReturnsFalse(string? schema)
    {
        var success = TransportProtocol.TryParse(schema, out _);

        Assert.False(success);
    }

    #endregion

    #region Classification properties

    [Fact]
    public void File_Classification_IsCorrect()
    {
        var protocol = TransportProtocol.File;

        Assert.True(protocol.IsFile);
        Assert.False(protocol.IsSocket);
    }

    [Fact]
    public void Socket_Classification_IsCorrect()
    {
        var protocol = TransportProtocol.Socket;

        Assert.False(protocol.IsFile);
        Assert.True(protocol.IsSocket);
    }

    #endregion

    #region ToString tests

    [Fact]
    public void ToString_File_ReturnsSchema()
    {
        Assert.Equal("file", TransportProtocol.File.ToString());
    }

    [Fact]
    public void ToString_Socket_ReturnsSchema()
    {
        Assert.Equal("socket", TransportProtocol.Socket.ToString());
    }

    #endregion

    #region Parse tests

    [Fact]
    public void Parse_ValidSchema_ReturnsProtocol()
    {
        var protocol = TransportProtocol.Parse("file", null);

        Assert.Equal(TransportKind.File, protocol.Kind);
    }

    [Fact]
    public void Parse_InvalidSchema_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => TransportProtocol.Parse("invalid", null));
    }

    #endregion

    #region Static instances

    [Fact]
    public void StaticInstances_AreCorrect()
    {
        Assert.Equal(TransportKind.File, TransportProtocol.File.Kind);
        Assert.Equal("file", TransportProtocol.File.Schema);

        Assert.Equal(TransportKind.Socket, TransportProtocol.Socket.Kind);
        Assert.Equal("socket", TransportProtocol.Socket.Schema);
    }

    #endregion
}
