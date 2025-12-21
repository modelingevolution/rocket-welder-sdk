using System;
using RocketWelder.SDK;
using Xunit;

namespace RocketWelder.SDK.Tests.HighLevel;

public class KeyPointsConnectionStringTests
{
    #region Parse - File protocol

    [Fact]
    public void Parse_FileWithAbsolutePath_ParsesCorrectly()
    {
        var cs = KeyPointsConnectionString.Parse("file:///home/user/output.bin", null);

        Assert.Equal(TransportKind.File, cs.Protocol.Kind);
        Assert.Equal("/home/user/output.bin", cs.Address);
        Assert.Equal(300, cs.MasterFrameInterval); // default
    }

    [Fact]
    public void Parse_FileWithRelativePath_ParsesCorrectly()
    {
        var cs = KeyPointsConnectionString.Parse("file://relative/path.bin", null);

        Assert.Equal(TransportKind.File, cs.Protocol.Kind);
        Assert.Equal("/relative/path.bin", cs.Address);
    }

    #endregion

    #region Parse - Socket protocol

    [Fact]
    public void Parse_Socket_ParsesCorrectly()
    {
        var cs = KeyPointsConnectionString.Parse("socket:///tmp/keypoints.sock", null);

        Assert.Equal(TransportKind.Socket, cs.Protocol.Kind);
        Assert.Equal("/tmp/keypoints.sock", cs.Address);
    }

    #endregion

    #region Parse - NNG protocols

    [Fact]
    public void Parse_NngPushIpc_ParsesCorrectly()
    {
        var cs = KeyPointsConnectionString.Parse("nng+push+ipc://tmp/keypoints", null);

        Assert.Equal(TransportKind.NngPushIpc, cs.Protocol.Kind);
        Assert.Equal("ipc:///tmp/keypoints", cs.Address);
    }

    [Fact]
    public void Parse_NngPushTcp_ParsesCorrectly()
    {
        var cs = KeyPointsConnectionString.Parse("nng+push+tcp://localhost:5555", null);

        Assert.Equal(TransportKind.NngPushTcp, cs.Protocol.Kind);
        Assert.Equal("tcp://localhost:5555", cs.Address);
    }

    [Fact]
    public void Parse_NngPubIpc_ParsesCorrectly()
    {
        var cs = KeyPointsConnectionString.Parse("nng+pub+ipc://tmp/keypoints", null);

        Assert.Equal(TransportKind.NngPubIpc, cs.Protocol.Kind);
        Assert.Equal("ipc:///tmp/keypoints", cs.Address);
    }

    #endregion

    #region Parse - Query parameters

    [Fact]
    public void Parse_WithMasterFrameInterval_ParsesParameter()
    {
        var cs = KeyPointsConnectionString.Parse("nng+push+ipc://tmp/kp?masterFrameInterval=500", null);

        Assert.Equal(500, cs.MasterFrameInterval);
    }

    [Fact]
    public void Parse_WithMultipleParameters_ParsesAll()
    {
        var cs = KeyPointsConnectionString.Parse("nng+push+ipc://tmp/kp?masterFrameInterval=100&custom=value", null);

        Assert.Equal(100, cs.MasterFrameInterval);
        Assert.True(cs.Parameters.ContainsKey("custom"));
        Assert.Equal("value", cs.Parameters["custom"]);
    }

    #endregion

    #region Parse - Invalid input

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("invalid")]
    [InlineData("unknown://path")]
    [InlineData("nng://path")] // incomplete
    public void Parse_InvalidConnectionString_ThrowsFormatException(string? input)
    {
        Assert.Throws<FormatException>(() => KeyPointsConnectionString.Parse(input!, null));
    }

    #endregion

    #region Default and FromEnvironment

    [Fact]
    public void Default_ReturnsValidConnectionString()
    {
        var cs = KeyPointsConnectionString.Default;

        Assert.Equal(TransportKind.NngPushIpc, cs.Protocol.Kind);
        Assert.Contains("keypoints", cs.Address);
        Assert.Equal(300, cs.MasterFrameInterval);
    }

    [Fact]
    public void FromEnvironment_WhenNotSet_ReturnsDefault()
    {
        var uniqueVar = $"KEYPOINTS_TEST_{Guid.NewGuid():N}";

        var cs = KeyPointsConnectionString.FromEnvironment(uniqueVar);

        Assert.Equal(KeyPointsConnectionString.Default.Protocol, cs.Protocol);
    }

    [Fact]
    public void FromEnvironment_WhenSet_ParsesEnvironmentVariable()
    {
        var uniqueVar = $"KEYPOINTS_TEST_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(uniqueVar, "socket:///tmp/test.sock");

        try
        {
            var cs = KeyPointsConnectionString.FromEnvironment(uniqueVar);

            Assert.Equal(TransportKind.Socket, cs.Protocol.Kind);
            Assert.Equal("/tmp/test.sock", cs.Address);
        }
        finally
        {
            Environment.SetEnvironmentVariable(uniqueVar, null);
        }
    }

    #endregion

    #region ToString and implicit conversion

    [Fact]
    public void ToString_ReturnsOriginalValue()
    {
        var input = "nng+push+ipc://tmp/keypoints?masterFrameInterval=300";
        var cs = KeyPointsConnectionString.Parse(input, null);

        Assert.Equal(input, cs.ToString());
    }

    [Fact]
    public void ImplicitConversion_ReturnsValue()
    {
        var cs = KeyPointsConnectionString.Parse("file:///path/to/file", null);
        string value = cs;

        Assert.Equal("file:///path/to/file", value);
    }

    #endregion
}
