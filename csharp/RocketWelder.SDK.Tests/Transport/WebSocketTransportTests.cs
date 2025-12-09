using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using RocketWelder.SDK.Transport;
using Xunit;
using Xunit.Abstractions;

namespace RocketWelder.SDK.Tests.Transport;

/// <summary>
/// Tests for WebSocket transport.
/// Integration tests use a minimal WebHost with mapped WebSocket handler.
/// </summary>
public class WebSocketTransportTests
{
    private readonly ITestOutputHelper _output;

    public WebSocketTransportTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void WebSocketFrameSink_Constructor_ThrowsOnNullSocket()
    {
        Assert.Throws<ArgumentNullException>(() => new WebSocketFrameSink(null!));
    }

    [Fact]
    public void WebSocketFrameSource_Constructor_ThrowsOnNullSocket()
    {
        Assert.Throws<ArgumentNullException>(() => new WebSocketFrameSource(null!));
    }

    [Fact]
    public async void WebSocketFrameSink_WriteFrame_ThrowsWhenDisposed()
    {
        var ws = new ClientWebSocket();
        var sink = new WebSocketFrameSink(ws);
        sink.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await sink.WriteFrameAsync(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void WebSocketFrameSource_ReadFrame_ThrowsWhenDisposed()
    {
        var ws = new ClientWebSocket();
        var source = new WebSocketFrameSource(ws);
        source.Dispose();

        Assert.Throws<ObjectDisposedException>(() => source.ReadFrame());
    }

    [Fact]
    public void WebSocketFrameSink_Flush_DoesNothing()
    {
        var ws = new ClientWebSocket();
        using var sink = new WebSocketFrameSink(ws, leaveOpen: true);
        sink.Flush();
        _output.WriteLine("Flush completed without exception");
    }

    [Fact]
    public async void WebSocketFrameSink_FlushAsync_ReturnsCompletedTask()
    {
        var ws = new ClientWebSocket();
        using var sink = new WebSocketFrameSink(ws, leaveOpen: true);
        await sink.FlushAsync();
        _output.WriteLine("FlushAsync completed without exception");
    }

    [Fact]
    public void WebSocketFrameSource_HasMoreFrames_ReturnsFalseWhenNotConnected()
    {
        var ws = new ClientWebSocket();
        using var source = new WebSocketFrameSource(ws, leaveOpen: true);
        // ClientWebSocket starts in None state, not Open
        Assert.False(source.HasMoreFrames);
        _output.WriteLine("HasMoreFrames correctly returns false for non-connected socket");
    }

    [Fact]
    public void WebSocketFrameSink_LeaveOpen_RespectsDisposal()
    {
        var ws = new ClientWebSocket();

        // With leaveOpen: true, disposing sink should not close the WebSocket
        using (var sink = new WebSocketFrameSink(ws, leaveOpen: true))
        {
            // Sink is created
        }
        // WebSocket should still be in its initial state (not disposed)
        Assert.Equal(WebSocketState.None, ws.State);

        _output.WriteLine("leaveOpen=true correctly leaves WebSocket open");
    }

    [Fact]
    public void WebSocketFrameSource_LeaveOpen_RespectsDisposal()
    {
        var ws = new ClientWebSocket();

        // With leaveOpen: true, disposing source should not close the WebSocket
        using (var source = new WebSocketFrameSource(ws, leaveOpen: true))
        {
            // Source is created
        }
        // WebSocket should still be in its initial state (not disposed)
        Assert.Equal(WebSocketState.None, ws.State);

        _output.WriteLine("leaveOpen=true correctly leaves WebSocket open");
    }

    #region Integration Tests with Minimal WebHost

    /// <summary>
    /// Creates a minimal WebHost with WebSocket echo handler.
    /// </summary>
    private static async Task<(IHost host, int port)> CreateWebSocketServerAsync()
    {
        var port = 17000 + Random.Shared.Next(1000);
        var host = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseUrls($"http://localhost:{port}");
                webBuilder.Configure(app =>
                {
                    app.UseWebSockets();
                    app.Map("/ws", wsApp =>
                    {
                        wsApp.Run(async context =>
                        {
                            if (context.WebSockets.IsWebSocketRequest)
                            {
                                using var ws = await context.WebSockets.AcceptWebSocketAsync();
                                await EchoHandler(ws);
                            }
                            else
                            {
                                context.Response.StatusCode = 400;
                            }
                        });
                    });
                });
            })
            .Build();

        await host.StartAsync();
        return (host, port);
    }

    private static async Task EchoHandler(WebSocket ws)
    {
        var buffer = new byte[64 * 1024];
        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                break;
            }
            // Echo back
            await ws.SendAsync(
                new ArraySegment<byte>(buffer, 0, result.Count),
                result.MessageType,
                result.EndOfMessage,
                CancellationToken.None);
        }
    }

    [Trait("Category", "Integration")]
    [Fact]
    public async Task WebSocket_Integration_RoundTrip()
    {
        // Arrange
        var (host, port) = await CreateWebSocketServerAsync();
        try
        {
            var testData = Encoding.UTF8.GetBytes("Hello WebSocket!");

            using var client = new ClientWebSocket();
            await client.ConnectAsync(new Uri($"ws://localhost:{port}/ws"), CancellationToken.None);

            using var sink = new WebSocketFrameSink(client, leaveOpen: true);
            using var source = new WebSocketFrameSource(client, leaveOpen: true);

            // Act
            await sink.WriteFrameAsync(testData);
            var received = source.ReadFrame();

            // Assert
            Assert.Equal(testData, received.ToArray());
            _output.WriteLine($"✓ Round-trip successful: {Encoding.UTF8.GetString(received.Span)}");

            await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Trait("Category", "Integration")]
    [Fact]
    public async Task WebSocket_Integration_MultipleMessages()
    {
        // Arrange
        var (host, port) = await CreateWebSocketServerAsync();
        try
        {
            var messages = new[]
            {
                Encoding.UTF8.GetBytes("Message 1"),
                Encoding.UTF8.GetBytes("Message 2"),
                Encoding.UTF8.GetBytes("Message 3")
            };

            using var client = new ClientWebSocket();
            await client.ConnectAsync(new Uri($"ws://localhost:{port}/ws"), CancellationToken.None);

            using var sink = new WebSocketFrameSink(client, leaveOpen: true);
            using var source = new WebSocketFrameSource(client, leaveOpen: true);

            // Act & Assert - send and receive each message
            foreach (var msg in messages)
            {
                await sink.WriteFrameAsync(msg);
                var received = source.ReadFrame();
                Assert.Equal(msg, received.ToArray());
                _output.WriteLine($"✓ Received: {Encoding.UTF8.GetString(received.Span)}");
            }

            await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Trait("Category", "Integration")]
    [Fact]
    public async Task WebSocket_Integration_LargeMessage()
    {
        // Arrange
        var (host, port) = await CreateWebSocketServerAsync();
        try
        {
            // 1MB message
            var largeData = new byte[1024 * 1024];
            Random.Shared.NextBytes(largeData);

            using var client = new ClientWebSocket();
            await client.ConnectAsync(new Uri($"ws://localhost:{port}/ws"), CancellationToken.None);

            using var sink = new WebSocketFrameSink(client, leaveOpen: true);
            using var source = new WebSocketFrameSource(client, leaveOpen: true);

            // Act
            await sink.WriteFrameAsync(largeData);
            var received = source.ReadFrame();

            // Assert
            Assert.Equal(largeData.Length, received.Length);
            Assert.Equal(largeData, received.ToArray());
            _output.WriteLine($"✓ Large message round-trip successful: {largeData.Length} bytes");

            await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    #endregion
}
