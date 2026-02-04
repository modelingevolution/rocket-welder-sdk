using System.Diagnostics;
using System.Drawing;
using System.Text;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using RocketWelder.SDK.Protocols;
using RocketWelder.SDK.Server;
using RocketWelder.SDK.Transport;
using Xunit;
using Xunit.Abstractions;

namespace RocketWelder.SDK.Server.Tests;

/// <summary>
/// BDD-style End-to-End tests for Server + Python SDK communication.
/// Tests the full pipeline: C# Server sends frames -> Python detects balls ->
/// Python writes keypoints/segmentation -> C# receives and verifies.
/// </summary>
public class BallDetectorE2ETests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testChannelName;
    private readonly string _keypointsSocketPath;
    private readonly string _segmentationSocketPath;
    private readonly string _pythonScriptPath;
    private readonly string _pythonVenvPath;

    public BallDetectorE2ETests(ITestOutputHelper output)
    {
        _output = output;
        _testChannelName = $"e2e-test-{Guid.NewGuid():N}";
        _keypointsSocketPath = $"/tmp/e2e-keypoints-{Guid.NewGuid():N}.sock";
        _segmentationSocketPath = $"/tmp/e2e-segmentation-{Guid.NewGuid():N}.sock";

        // Paths relative to test project
        var baseDir = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..", "python"));
        _pythonScriptPath = Path.Combine(baseDir, "examples", "05-ball-detector", "main.py");
        _pythonVenvPath = Path.Combine(baseDir, "venv", "bin", "python");
    }

    public void Dispose()
    {
        // Clean up socket files
        if (File.Exists(_keypointsSocketPath))
            File.Delete(_keypointsSocketPath);
        if (File.Exists(_segmentationSocketPath))
            File.Delete(_segmentationSocketPath);

        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    /// <summary>
    /// Expected ball positions for test frames.
    /// </summary>
    private static readonly (int X, int Y, int Radius, MCvScalar Color)[] TestBalls = new[]
    {
        (160, 120, 40, new MCvScalar(0, 0, 255)),    // Red ball, center of frame 1
        (100, 80, 30, new MCvScalar(255, 0, 0)),    // Blue ball, top-left area
        (220, 160, 50, new MCvScalar(0, 255, 0)),   // Green ball, bottom-right area
    };

    /// <summary>
    /// Creates a test frame with a colored ball at specified position.
    /// </summary>
    private Mat CreateTestFrame(int width, int height, int ballX, int ballY, int radius, MCvScalar color)
    {
        var frame = new Mat(height, width, DepthType.Cv8U, 3);
        frame.SetTo(new MCvScalar(64, 64, 64)); // Dark gray background

        // Draw filled circle (ball)
        CvInvoke.Circle(frame, new Point(ballX, ballY), radius, color, -1);

        // Add some texture to help detection
        CvInvoke.Circle(frame, new Point(ballX, ballY), radius, new MCvScalar(255, 255, 255), 2);

        return frame;
    }

    [Fact]
    public async Task Given_ThreeFramesWithBalls_When_PythonProcessesThem_Then_KeyPointsAndSegmentationAreCorrect()
    {
        // =========================================
        // GIVEN: Test setup with 3 frames containing balls
        // =========================================
        const int width = 320;
        const int height = 240;
        const int frameCount = 3;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        _output.WriteLine("=== BDD E2E Test: Ball Detection ===");
        _output.WriteLine($"Channel: {_testChannelName}");
        _output.WriteLine($"KeyPoints socket: {_keypointsSocketPath}");
        _output.WriteLine($"Segmentation socket: {_segmentationSocketPath}");

        // Verify Python environment exists
        if (!File.Exists(_pythonVenvPath))
        {
            _output.WriteLine($"Python venv not found at: {_pythonVenvPath}");
            Assert.Fail("Python venv not found - test skipped");
            return;
        }

        // Create test frames
        var testFrames = new List<(Mat Frame, int ExpectedX, int ExpectedY, int ExpectedRadius)>();
        for (int i = 0; i < frameCount; i++)
        {
            var ball = TestBalls[i % TestBalls.Length];
            var frame = CreateTestFrame(width, height, ball.X, ball.Y, ball.Radius, ball.Color);
            testFrames.Add((frame, ball.X, ball.Y, ball.Radius));
            _output.WriteLine($"Frame {i + 1}: Ball at ({ball.X}, {ball.Y}) radius {ball.Radius}");
        }

        // =========================================
        // WHEN: Start Python detector and send frames
        // =========================================
        _output.WriteLine("\n--- Starting Python Ball Detector ---");

        // Use extended timeout (30s) so Python has time to connect after C# Server starts
        var connectionString = $"shm://{_testChannelName}?size=4MB&metadata=4KB&mode=Duplex&timeout=30000";

        // Start Python process in background
        var pythonProcess = StartPythonDetectorBackground(
            connectionString,
            _keypointsSocketPath,
            _segmentationSocketPath,
            frameCount);

        // Wait briefly for Python to create _request shared memory buffer
        _output.WriteLine("Waiting for Python to create shared memory...");
        var bufferPath = $"/dev/shm/{_testChannelName}_request";
        var waitDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!File.Exists(bufferPath) && DateTime.UtcNow < waitDeadline)
        {
            await Task.Delay(50, cts.Token);
        }

        if (!File.Exists(bufferPath))
        {
            // Check Python output for errors
            var stdout = pythonProcess.StandardOutput.ReadToEnd();
            var stderr = pythonProcess.StandardError.ReadToEnd();
            _output.WriteLine($"Python stdout: {stdout}");
            _output.WriteLine($"Python stderr: {stderr}");
            throw new Exception($"Python failed to create shared memory buffer at {bufferPath}");
        }
        _output.WriteLine($"Shared memory buffer found: {bufferPath}");

        // Start C# Server immediately - it creates _response buffer that Python is waiting for
        // Create Server and connect
        _output.WriteLine("Creating C# Server...");
        using var server = new Server(_testChannelName, width, height, "BGR");

        try
        {
            server.Start(TimeSpan.FromSeconds(15));
            _output.WriteLine($"Server started, client connected: {server.IsClientConnected}");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Server failed to start: {ex.Message}");
            throw;
        }

        // Debug: Check both shared memory buffers exist
        var requestBufferPath = $"/dev/shm/{_testChannelName}_request";
        var responseBufferPath = $"/dev/shm/{_testChannelName}_response";
        _output.WriteLine($"Request buffer exists: {File.Exists(requestBufferPath)}");
        _output.WriteLine($"Response buffer exists: {File.Exists(responseBufferPath)}");

        // Wait for connection
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!server.IsClientConnected && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, cts.Token);
        }

        if (!server.IsClientConnected)
        {
            _output.WriteLine("ERROR: Client did not connect");
            throw new Exception("Python client did not connect");
        }

        // Wait for Python to bind socket files (Python binds after shared memory is connected)
        _output.WriteLine("Waiting for socket files...");
        var socketWaitDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!File.Exists(_keypointsSocketPath) && DateTime.UtcNow < socketWaitDeadline)
        {
            await Task.Delay(50, cts.Token);
        }

        if (!File.Exists(_keypointsSocketPath))
            throw new Exception($"KeyPoints socket not created: {_keypointsSocketPath}");
        _output.WriteLine($"KeyPoints socket file exists: {_keypointsSocketPath}");

        // Connect to keypoints socket
        await server.ConnectToKeyPointsSocketAsync(_keypointsSocketPath, TimeSpan.FromSeconds(10), cts.Token);
        _output.WriteLine("Connected to keypoints socket");

        // Wait for segmentation socket file
        socketWaitDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!File.Exists(_segmentationSocketPath) && DateTime.UtcNow < socketWaitDeadline)
        {
            await Task.Delay(50, cts.Token);
        }

        if (!File.Exists(_segmentationSocketPath))
            throw new Exception($"Segmentation socket not created: {_segmentationSocketPath}");
        _output.WriteLine($"Segmentation socket file exists: {_segmentationSocketPath}");

        // Connect to segmentation socket
        await server.ConnectToSegmentationSocketAsync(_segmentationSocketPath, TimeSpan.FromSeconds(10), cts.Token);
        _output.WriteLine("Connected to segmentation socket");

        // Debug: Wait a bit to allow Python to fully initialize
        _output.WriteLine("Waiting 2s for Python to fully initialize...");
        await Task.Delay(2000, cts.Token);

        // Debug: Check process state and any early output
        pythonProcess.Refresh();
        _output.WriteLine($"Python process running: {!pythonProcess.HasExited}, PID: {pythonProcess.Id}");

        // Capture any Python output so far
        StringBuilder stdoutBuffer = new StringBuilder();
        StringBuilder stderrBuffer = new StringBuilder();

        pythonProcess.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null) stdoutBuffer.AppendLine(e.Data);
        };
        pythonProcess.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null) stderrBuffer.AppendLine(e.Data);
        };
        pythonProcess.BeginOutputReadLine();
        pythonProcess.BeginErrorReadLine();

        // Wait another second to capture initial Python logs
        await Task.Delay(1000, cts.Token);
        _output.WriteLine($"Python stdout so far:\n{stdoutBuffer}");
        _output.WriteLine($"Python stderr so far:\n{stderrBuffer}");

        // Send frames and collect results
        var latencies = new List<double>();
        var receivedKeyPoints = new List<DeltaFrame<KeyPoint>>();
        var receivedSegmentations = new List<RocketWelder.SDK.Protocols.SegmentationFrame>();

        _output.WriteLine("\n--- Sending Frames ---");

        for (int i = 0; i < frameCount; i++)
        {
            var (frame, expectedX, expectedY, expectedRadius) = testFrames[i];

            var sw = Stopwatch.StartNew();

            // Send frame
            _output.WriteLine($"Sending frame {i + 1}...");
            using var response = server.SendFrame(frame, TimeSpan.FromSeconds(10));

            sw.Stop();
            latencies.Add(sw.Elapsed.TotalMilliseconds);

            _output.WriteLine($"  Response received in {sw.Elapsed.TotalMilliseconds:F1}ms");

            // Read keypoints
            await Task.Delay(100, cts.Token); // Give Python time to write results
            var kpFrame = server.TryReadKeyPointsFrame(TimeSpan.FromSeconds(5));
            if (kpFrame.HasValue)
            {
                receivedKeyPoints.Add(kpFrame.Value);
                _output.WriteLine($"  KeyPoints: FrameId={kpFrame.Value.FrameId}, Count={kpFrame.Value.Items.Length}");
            }

            // Read segmentation
            var segFrame = server.TryReadSegmentationFrame(TimeSpan.FromSeconds(5));
            if (segFrame.HasValue)
            {
                receivedSegmentations.Add(segFrame.Value);
                _output.WriteLine($"  Segmentation: FrameId={segFrame.Value.FrameId}, Instances={segFrame.Value.Instances.Count}");
            }
        }

        // Stop Python
        cts.Cancel();
        try
        {
            pythonProcess.Kill();
            pythonProcess.WaitForExit(5000);
        }
        catch { }

        // Log Python output
        try
        {
            var stdout = pythonProcess.StandardOutput.ReadToEnd();
            var stderr = pythonProcess.StandardError.ReadToEnd();
            if (!string.IsNullOrWhiteSpace(stdout))
                _output.WriteLine($"Python stdout:\n{stdout}");
            if (!string.IsNullOrWhiteSpace(stderr))
                _output.WriteLine($"Python stderr:\n{stderr}");
        }
        catch { }

        // =========================================
        // THEN: Verify results
        // =========================================
        _output.WriteLine("\n--- Verification ---");

        // Verify we received results for all frames
        Assert.Equal(frameCount, (int)server.FrameNumber);
        _output.WriteLine($"[PASS] Sent {frameCount} frames");

        // Verify keypoints were detected
        Assert.NotEmpty(receivedKeyPoints);
        _output.WriteLine($"[PASS] Received {receivedKeyPoints.Count} keypoint frames");

        // Verify segmentation was detected
        Assert.NotEmpty(receivedSegmentations);
        _output.WriteLine($"[PASS] Received {receivedSegmentations.Count} segmentation frames");

        // Verify keypoint positions are approximately correct (within tolerance)
        const int positionTolerance = 30; // pixels

        for (int i = 0; i < Math.Min(receivedKeyPoints.Count, frameCount); i++)
        {
            var kpFrame = receivedKeyPoints[i];
            var (_, expectedX, expectedY, _) = testFrames[i];

            if (kpFrame.Items.Length > 0)
            {
                var detectedKp = kpFrame.Items.Span[0];
                var distanceX = Math.Abs(detectedKp.Position.X - expectedX);
                var distanceY = Math.Abs(detectedKp.Position.Y - expectedY);

                _output.WriteLine($"Frame {i + 1}: Expected ({expectedX}, {expectedY}), " +
                    $"Detected ({detectedKp.Position.X}, {detectedKp.Position.Y}), " +
                    $"Distance: ({distanceX}, {distanceY})");

                Assert.True(distanceX <= positionTolerance,
                    $"X position mismatch: expected ~{expectedX}, got {detectedKp.Position.X}");
                Assert.True(distanceY <= positionTolerance,
                    $"Y position mismatch: expected ~{expectedY}, got {detectedKp.Position.Y}");
            }
        }
        _output.WriteLine($"[PASS] KeyPoint positions within {positionTolerance}px tolerance");

        // Report latency stats
        _output.WriteLine("\n--- Latency Stats ---");
        _output.WriteLine($"  Average: {latencies.Average():F1}ms");
        _output.WriteLine($"  Min: {latencies.Min():F1}ms");
        _output.WriteLine($"  Max: {latencies.Max():F1}ms");

        // Cleanup test frames
        foreach (var (frame, _, _, _) in testFrames)
        {
            frame.Dispose();
        }

        _output.WriteLine("\n=== TEST PASSED ===");
    }

    [Fact]
    public async Task Given_MockBallDetector_When_ProcessingFrames_Then_ResultsAreReceived()
    {
        // This test uses a simpler approach without Python -
        // tests the C# Server's ability to receive keypoints/segmentation from sockets

        const int width = 320;
        const int height = 240;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        _output.WriteLine("=== Mock Ball Detector Test ===");

        // Create server
        using var server = new Server(_testChannelName, width, height, "BGR");

        // Simulate keypoints being written by "Python"
        var keypointsWriterTask = Task.Run(async () =>
        {
            using var sink = UnixSocketFrameSink.Bind(_keypointsSocketPath);
            _output.WriteLine("Mock keypoints sink bound");

            await Task.Delay(1000, cts.Token); // Wait for server to connect

            // Write 3 keypoint frames
            for (int frameId = 1; frameId <= 3; frameId++)
            {
                var keypoints = new KeyPoint[]
                {
                    new(0, 160 + frameId * 10, 120 + frameId * 5, 9500), // 0.95 as ushort (0-10000)
                };

                var buffer = new byte[KeyPointsProtocol.CalculateMasterFrameSize(keypoints.Length)];
                var bytesWritten = KeyPointsProtocol.WriteMasterFrame(buffer, (ulong)frameId, keypoints);
                sink.WriteFrame(buffer.AsSpan(0, bytesWritten).ToArray());
                sink.Flush();

                _output.WriteLine($"Wrote keypoints for frame {frameId}");
                await Task.Delay(100, cts.Token);
            }
        }, cts.Token);

        // Simulate segmentation being written by "Python"
        var segmentationWriterTask = Task.Run(async () =>
        {
            using var sink = UnixSocketFrameSink.Bind(_segmentationSocketPath);
            _output.WriteLine("Mock segmentation sink bound");

            await Task.Delay(1000, cts.Token); // Wait for server to connect

            // Write 3 segmentation frames
            for (int frameId = 1; frameId <= 3; frameId++)
            {
                var instances = new RocketWelder.SDK.Protocols.SegmentationInstance[]
                {
                    new(classId: 1, instanceId: 0, Confidence.Full, new Point[]
                    {
                        new(150, 110), new(170, 110), new(170, 130), new(150, 130)
                    }),
                };

                var frame = new RocketWelder.SDK.Protocols.SegmentationFrame(
                    (ulong)frameId, (uint)width, (uint)height, instances);
                var buffer = new byte[1024];
                var bytesWritten = SegmentationProtocol.Write(buffer, frame);
                sink.WriteFrame(buffer.AsSpan(0, bytesWritten).ToArray());
                sink.Flush();

                _output.WriteLine($"Wrote segmentation for frame {frameId}");
                await Task.Delay(100, cts.Token);
            }
        }, cts.Token);

        // Give mock writers time to bind
        await Task.Delay(500, cts.Token);

        // Connect to sockets
        await server.ConnectToKeyPointsSocketAsync(_keypointsSocketPath, TimeSpan.FromSeconds(5), cts.Token);
        _output.WriteLine("Connected to keypoints socket");

        await server.ConnectToSegmentationSocketAsync(_segmentationSocketPath, TimeSpan.FromSeconds(5), cts.Token);
        _output.WriteLine("Connected to segmentation socket");

        // Read results
        var receivedKeyPoints = new List<DeltaFrame<KeyPoint>>();
        var receivedSegmentations = new List<RocketWelder.SDK.Protocols.SegmentationFrame>();

        for (int i = 0; i < 3; i++)
        {
            await Task.Delay(200, cts.Token);

            var kp = server.TryReadKeyPointsFrame(TimeSpan.FromSeconds(2));
            if (kp.HasValue)
            {
                receivedKeyPoints.Add(kp.Value);
                _output.WriteLine($"Received keypoints frame {kp.Value.FrameId}");
            }

            var seg = server.TryReadSegmentationFrame(TimeSpan.FromSeconds(2));
            if (seg.HasValue)
            {
                receivedSegmentations.Add(seg.Value);
                _output.WriteLine($"Received segmentation frame {seg.Value.FrameId}");
            }
        }

        // Cleanup
        cts.Cancel();
        try { await keypointsWriterTask; } catch { }
        try { await segmentationWriterTask; } catch { }

        // Verify
        Assert.Equal(3, receivedKeyPoints.Count);
        Assert.Equal(3, receivedSegmentations.Count);

        _output.WriteLine("\n=== TEST PASSED ===");
    }

    private Process StartPythonDetectorBackground(
        string connectionString,
        string keypointsSocket,
        string segmentationSocket,
        int exitAfter)
    {
        var args = $"\"{_pythonScriptPath}\" \"{connectionString}\" " +
            $"--keypoints-socket \"{keypointsSocket}\" " +
            $"--segmentation-socket \"{segmentationSocket}\" " +
            $"--exit-after {exitAfter} --debug";

        _output.WriteLine($"Starting Python: {_pythonVenvPath} {args}");

        var startInfo = new ProcessStartInfo
        {
            FileName = _pythonVenvPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        var process = new Process { StartInfo = startInfo };
        process.Start();

        _output.WriteLine($"Python process started with PID: {process.Id}");
        return process;
    }
}
