using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;
using RocketWelder.SDK.Protocols;
using RocketWelder.SDK.Server;
using RocketWelder.SDK.Transport;
using Xunit;
using Xunit.Abstractions;

namespace RocketWelder.SDK.Server.Tests;

/// <summary>
/// Integration tests for Server + RocketWelderClient communication.
/// These tests verify the full duplex communication path.
/// </summary>
public class ServerIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testChannelName;

    public ServerIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _testChannelName = $"test-channel-{Guid.NewGuid():N}";
    }

    public void Dispose()
    {
        // Clean up any leftover shared memory
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    [Fact]
    public async Task Server_And_Client_CanCommunicate_ViaSharedMemory()
    {
        // Arrange
        const int width = 320;
        const int height = 240;
        const string format = "RGB";
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        _output.WriteLine($"Creating test with channel: {_testChannelName}");

        // Create client connection string
        var connectionString = $"shm://{_testChannelName}?size=4MB&metadata=4KB&mode=Duplex";
        _output.WriteLine($"Client connection string: {connectionString}");

        // SDK Client must start FIRST - it creates the shared memory
        // Server connects to existing shared memory (like GStreamer does)
        Mat? receivedFrame = null;
        var frameReceived = new TaskCompletionSource<bool>();
        var clientStarted = new TaskCompletionSource<bool>();
        Exception? clientException = null;

        RocketWelderClient? sdkClient = null;
        var clientTask = Task.Run(async () =>
        {
            try
            {
                _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] SDK Client task starting...");
                sdkClient = RocketWelderClient.FromConnectionString(connectionString);
                _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] SDK Client created, calling Start()...");

                // Start() starts background processing and returns immediately
                // The CancellationToken is used to stop the background thread
                sdkClient.Start((input, output) =>
                {
                    _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client received frame: {input.Width}x{input.Height}");
                    receivedFrame = input.Clone();
                    // Simply copy input to output
                    input.CopyTo(output);
                    // Signal that we processed a frame
                    frameReceived.TrySetResult(true);
                }, cts.Token);
                _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] SDK Client Start() returned, background processing started");

                // Signal that client is running
                clientStarted.SetResult(true);

                // Wait until cancelled - keep the client alive!
                _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] SDK Client waiting for cancellation...");
                try
                {
                    await Task.Delay(Timeout.Infinite, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] SDK Client received cancellation signal");
                }

                // Now we can stop and dispose
                _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] SDK Client stopping...");
                sdkClient.Stop();
            }
            catch (OperationCanceledException)
            {
                _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] SDK Client cancelled normally");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] SDK Client FAILED: {ex.GetType().Name}: {ex.Message}");
                _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] SDK Client stack: {ex.StackTrace}");
                clientException = ex;
                clientStarted.TrySetException(ex);
            }
            finally
            {
                sdkClient?.Dispose();
                _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] SDK Client disposed");
            }
        }, cts.Token);

        // Wait for client to signal it's about to start (or fail)
        try
        {
            await clientStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] SDK Client signaled starting, waiting for shared memory creation...");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] SDK Client failed to start: {ex.Message}");
            throw;
        }

        // Give SDK time to create shared memory (CreateImmutableServer happens after Start() is called)
        _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Waiting 2s for shared memory creation...");
        await Task.Delay(2000, cts.Token);
        _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] SDK Client should have created shared memory by now");

        // Now Server can connect (acts as GStreamer - connects to existing shm)
        _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Creating Server...");
        using var server = new Server(_testChannelName, width, height, format);

        // Act - Start server (connects to shared memory created by client)
        _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Calling server.Start()...");
        server.Start(TimeSpan.FromSeconds(10));
        _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Server started, client connected: {server.IsClientConnected}");

        // Wait for client to be ready and connected
        _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Waiting for IsClientConnected...");
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!server.IsClientConnected && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, cts.Token);
        }
        _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] After waiting, client connected: {server.IsClientConnected}");

        if (!server.IsClientConnected)
        {
            // Check if client task failed
            if (clientException != null)
                throw new Exception($"SDK Client failed: {clientException.Message}", clientException);
            throw new Exception("SDK Client did not connect within timeout");
        }

        // Create test frame
        using var testFrame = new Mat(height, width, DepthType.Cv8U, 3);
        testFrame.SetTo(new Emgu.CV.Structure.MCvScalar(100, 150, 200)); // Set RGB values

        _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Sending frame to client...");
        using var response = server.SendFrame(testFrame, TimeSpan.FromSeconds(5));

        // Wait for client to process the frame
        _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Waiting for frameReceived signal...");
        await frameReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.NotNull(response);
        Assert.Equal(width, response.Width);
        Assert.Equal(height, response.Height);
        Assert.Equal(1UL, server.FrameNumber);
        Assert.NotNull(receivedFrame);

        _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Test passed! Response: {response.Width}x{response.Height}");

        // Clean up
        cts.Cancel();
        try { await clientTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task Server_CanRead_KeyPointsFromSocket()
    {
        // Arrange
        var socketPath = $"/tmp/test-keypoints-{Guid.NewGuid():N}.sock";
        _output.WriteLine($"Socket path: {socketPath}");

        // Create server
        using var server = new Server(_testChannelName, 640, 480);

        // Start a task that will bind to socket and write keypoints
        var writerTask = Task.Run(async () =>
        {
            // Create socket sink that binds (server mode)
            using var sink = UnixSocketFrameSink.Bind(socketPath);
            _output.WriteLine("KeyPoints sink bound, waiting for connection...");

            // Wait for server to connect
            await Task.Delay(500);

            // Write a keypoints frame
            var keypoints = new KeyPoint[]
            {
                new(0, 100, 200, 950),
                new(1, 150, 250, 900),
                new(2, 200, 300, 850)
            };

            var buffer = new byte[KeyPointsProtocol.CalculateMasterFrameSize(keypoints.Length)];
            var bytesWritten = KeyPointsProtocol.WriteMasterFrame(buffer, frameId: 42, keypoints);

            _output.WriteLine($"Writing {bytesWritten} bytes of keypoints data");
            sink.WriteFrame(buffer.AsSpan(0, bytesWritten).ToArray());
            sink.Flush();
        });

        // Give writer time to bind
        await Task.Delay(200);

        // Act - Connect to socket and read keypoints
        await server.ConnectToKeyPointsSocketAsync(socketPath, TimeSpan.FromSeconds(5));
        _output.WriteLine("Server connected to keypoints socket");

        // Wait for writer to send data
        await Task.Delay(500);

        var frame = server.TryReadKeyPointsFrame(TimeSpan.FromSeconds(5));

        // Assert
        Assert.NotNull(frame);
        Assert.Equal(42UL, frame.Value.FrameId);
        Assert.False(frame.Value.IsDelta);  // Master frame = not delta
        Assert.Equal(3, frame.Value.Items.Length);
        Assert.Equal(100, frame.Value.Items.Span[0].Position.X);
        Assert.Equal(200, frame.Value.Items.Span[0].Position.Y);

        _output.WriteLine($"Read keypoints frame: FrameId={frame.Value.FrameId}, Points={frame.Value.Items.Length}");

        await writerTask;

        // Cleanup
        if (File.Exists(socketPath))
            File.Delete(socketPath);
    }

    [Fact]
    public async Task Server_CanRead_SegmentationFromSocket()
    {
        // Arrange
        var socketPath = $"/tmp/test-segmentation-{Guid.NewGuid():N}.sock";
        _output.WriteLine($"Socket path: {socketPath}");

        // Create server
        using var server = new Server(_testChannelName, 640, 480);

        // Start a task that will bind to socket and write segmentation
        var writerTask = Task.Run(async () =>
        {
            // Create socket sink that binds (server mode)
            using var sink = UnixSocketFrameSink.Bind(socketPath);
            _output.WriteLine("Segmentation sink bound, waiting for connection...");

            // Wait for server to connect
            await Task.Delay(500);

            // Write a segmentation frame
            var instances = new RocketWelder.SDK.Protocols.SegmentationInstance[]
            {
                new(ClassId: 1, InstanceId: 0, new Point[] { new(10, 20), new(30, 40), new(50, 60) }),
                new(ClassId: 2, InstanceId: 1, new Point[] { new(100, 110), new(120, 130) })
            };

            var frame = new RocketWelder.SDK.Protocols.SegmentationFrame(FrameId: 123, Width: 640, Height: 480, instances);
            var buffer = new byte[1024];
            var bytesWritten = SegmentationProtocol.Write(buffer, frame);

            _output.WriteLine($"Writing {bytesWritten} bytes of segmentation data");
            sink.WriteFrame(buffer.AsSpan(0, bytesWritten).ToArray());
            sink.Flush();
        });

        // Give writer time to bind
        await Task.Delay(200);

        // Act - Connect to socket and read segmentation
        await server.ConnectToSegmentationSocketAsync(socketPath, TimeSpan.FromSeconds(5));
        _output.WriteLine("Server connected to segmentation socket");

        // Wait for writer to send data
        await Task.Delay(500);

        var result = server.TryReadSegmentationFrame(TimeSpan.FromSeconds(5));

        // Assert
        Assert.NotNull(result);
        Assert.Equal(123UL, result.Value.FrameId);
        Assert.Equal(640U, result.Value.Width);
        Assert.Equal(480U, result.Value.Height);
        Assert.Equal(2, result.Value.Instances.Count);
        Assert.Equal(1, result.Value.Instances[0].ClassId);
        Assert.Equal(3, result.Value.Instances[0].Points.Length);

        _output.WriteLine($"Read segmentation frame: FrameId={result.Value.FrameId}, Instances={result.Value.Instances.Count}");

        await writerTask;

        // Cleanup
        if (File.Exists(socketPath))
            File.Delete(socketPath);
    }
}
