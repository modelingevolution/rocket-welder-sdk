using System.Diagnostics;
using System.Drawing;
using RocketWelder.SDK.Blazor;
using RocketWelder.SDK.Protocols;
using RocketWelder.SDK.Tests.Blazor;
using RocketWelder.SDK.Transport;
using Xunit;
using Xunit.Abstractions;

namespace RocketWelder.SDK.Tests.Integration;

/// <summary>
/// Integration tests that spawn Python and verify C# decoders can correctly
/// decode keypoints and segmentation data sent over Unix sockets.
///
/// Tests the full pipeline:
/// Python SDK writes → Unix Socket → C# UnixSocketFrameSource → C# Decoder → Verification
/// </summary>
public class PythonDecoderIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _keypointsSocketPath;
    private readonly string _segmentationSocketPath;
    private readonly string _pythonScriptPath;
    private readonly string _pythonVenvPath;

    // Expected test data - must match Python script values
    private static readonly (int Id, int X, int Y, float Confidence)[] ExpectedKeyPoints =
    {
        (0, 100, 200, 0.95f),
        (1, 150, 250, 0.92f),
        (2, 120, 180, 0.88f),
    };

    private static readonly (int ClassId, int InstanceId, Point[] Points)[] ExpectedSegmentationInstances =
    {
        (1, 1, new[] { new Point(100, 100), new Point(200, 100), new Point(200, 200), new Point(100, 200) }),
        (2, 1, new[] { new Point(300, 300), new Point(350, 250), new Point(400, 300) }),
    };

    private const ulong ExpectedFrameId = 42;
    private const int ExpectedWidth = 1920;
    private const int ExpectedHeight = 1080;

    public PythonDecoderIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        var testId = Guid.NewGuid().ToString("N")[..8];
        _keypointsSocketPath = $"/tmp/python-decoder-test-kp-{testId}.sock";
        _segmentationSocketPath = $"/tmp/python-decoder-test-seg-{testId}.sock";

        // Paths relative to test project
        var baseDir = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..", "python"));
        _pythonScriptPath = Path.Combine(baseDir, "scripts", "unix_socket_protocol_writer.py");
        _pythonVenvPath = Path.Combine(baseDir, "venv", "bin", "python");
    }

    public void Dispose()
    {
        // Clean up socket files
        foreach (var path in new[] { _keypointsSocketPath, _segmentationSocketPath })
        {
            if (File.Exists(path))
            {
                try { File.Delete(path); }
                catch { /* ignore */ }
            }
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task PythonKeyPoints_DecodedByBlazorDecoder_MatchesExpectedValues()
    {
        // Skip if not on Linux/macOS
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            _output.WriteLine("Skipping test - Unix sockets not supported on this platform");
            return;
        }

        // Skip if Python venv not found
        if (!File.Exists(_pythonVenvPath))
        {
            _output.WriteLine($"Python venv not found at: {_pythonVenvPath}");
            _output.WriteLine("Skipping test - run 'cd python && python -m venv venv && pip install -e .' to set up");
            return;
        }

        if (!File.Exists(_pythonScriptPath))
        {
            _output.WriteLine($"Python script not found at: {_pythonScriptPath}");
            return;
        }

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        _output.WriteLine("=== Python KeyPoints Decoder Integration Test ===");
        _output.WriteLine($"KeyPoints socket: {_keypointsSocketPath}");

        // Start Python in background
        var pythonProcess = StartPythonWriter(frameCount: 1);

        try
        {
            // Wait for Python to create socket file
            await WaitForSocketFile(_keypointsSocketPath, TimeSpan.FromSeconds(10), cts.Token);
            _output.WriteLine("Socket file detected, connecting...");

            // Connect to keypoints socket
            using var source = await UnixSocketFrameSource.ConnectAsync(
                _keypointsSocketPath,
                TimeSpan.FromSeconds(5),
                retry: true);

            _output.WriteLine("Connected to keypoints socket");

            // Read frame
            var frameData = await source.ReadFrameAsync(cts.Token);
            Assert.False(frameData.IsEmpty, "No frame data received");
            _output.WriteLine($"Received {frameData.Length} bytes");

            // Decode using Blazor decoder
            var canvas = new MockCanvas();
            var stage = new MockStage(canvas);
            var decoder = new KeyPointsDecoder(stage);
            decoder.CrossSize = 6;

            var result = decoder.Decode(frameData.Span);

            // Verify decode success
            Assert.True(result.Success, "Decoder failed");
            Assert.Equal(ExpectedFrameId, result.FrameId);
            _output.WriteLine($"Decoded frame ID: {result.FrameId}");

            // Verify draw calls - each keypoint draws 2 lines (cross)
            Assert.Equal(ExpectedKeyPoints.Length * 2, canvas.LineCalls.Count);
            _output.WriteLine($"Canvas line calls: {canvas.LineCalls.Count}");

            // Verify keypoint positions via cross center coordinates
            for (int i = 0; i < ExpectedKeyPoints.Length; i++)
            {
                var expected = ExpectedKeyPoints[i];
                var horizontalLine = canvas.LineCalls[i * 2];     // Horizontal part of cross
                var verticalLine = canvas.LineCalls[i * 2 + 1];   // Vertical part of cross

                // Horizontal line: y should match, x should span around center
                Assert.Equal(expected.Y, horizontalLine.Y1);
                Assert.Equal(expected.X - decoder.CrossSize, horizontalLine.X1);
                Assert.Equal(expected.X + decoder.CrossSize, horizontalLine.X2);

                // Vertical line: x should match, y should span around center
                Assert.Equal(expected.X, verticalLine.X1);
                Assert.Equal(expected.Y - decoder.CrossSize, verticalLine.Y1);
                Assert.Equal(expected.Y + decoder.CrossSize, verticalLine.Y2);

                _output.WriteLine($"KeyPoint {expected.Id}: ({expected.X}, {expected.Y}) - VERIFIED");
            }

            _output.WriteLine("=== TEST PASSED ===");
        }
        finally
        {
            await StopPython(pythonProcess);
        }
    }

    [Fact]
    public async Task PythonSegmentation_DecodedByBlazorDecoder_MatchesExpectedValues()
    {
        // Skip if not on Linux/macOS
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            _output.WriteLine("Skipping test - Unix sockets not supported on this platform");
            return;
        }

        // Skip if Python venv not found
        if (!File.Exists(_pythonVenvPath))
        {
            _output.WriteLine($"Python venv not found at: {_pythonVenvPath}");
            _output.WriteLine("Skipping test - run 'cd python && python -m venv venv && pip install -e .' to set up");
            return;
        }

        if (!File.Exists(_pythonScriptPath))
        {
            _output.WriteLine($"Python script not found at: {_pythonScriptPath}");
            return;
        }

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        _output.WriteLine("=== Python Segmentation Decoder Integration Test ===");
        _output.WriteLine($"Segmentation socket: {_segmentationSocketPath}");

        // Start Python in background
        var pythonProcess = StartPythonWriter(frameCount: 1);

        try
        {
            // Wait for Python to create socket file
            await WaitForSocketFile(_segmentationSocketPath, TimeSpan.FromSeconds(10), cts.Token);
            _output.WriteLine("Socket file detected, connecting...");

            // Connect to segmentation socket
            using var source = await UnixSocketFrameSource.ConnectAsync(
                _segmentationSocketPath,
                TimeSpan.FromSeconds(5),
                retry: true);

            _output.WriteLine("Connected to segmentation socket");

            // Read frame
            var frameData = await source.ReadFrameAsync(cts.Token);
            Assert.False(frameData.IsEmpty, "No frame data received");
            _output.WriteLine($"Received {frameData.Length} bytes");

            // Decode using Blazor decoder
            var canvas = new MockCanvas();
            var stage = new MockStage(canvas);
            var decoder = new SegmentationDecoder(stage);

            var result = decoder.Decode(frameData.Span);

            // Verify decode success
            Assert.True(result.Success, "Decoder failed");
            Assert.Equal(ExpectedFrameId, result.FrameId);
            _output.WriteLine($"Decoded frame ID: {result.FrameId}");

            // Verify polygon calls
            Assert.Equal(ExpectedSegmentationInstances.Length, canvas.PolygonCalls.Count);
            _output.WriteLine($"Canvas polygon calls: {canvas.PolygonCalls.Count}");

            // Verify each polygon
            for (int i = 0; i < ExpectedSegmentationInstances.Length; i++)
            {
                var expected = ExpectedSegmentationInstances[i];
                var polygonCall = canvas.PolygonCalls[i];

                Assert.Equal(expected.Points.Length, polygonCall.Points.Length);

                for (int j = 0; j < expected.Points.Length; j++)
                {
                    Assert.Equal(expected.Points[j].X, (int)polygonCall.Points[j].X);
                    Assert.Equal(expected.Points[j].Y, (int)polygonCall.Points[j].Y);
                }

                _output.WriteLine($"Instance {i}: ClassId={expected.ClassId}, Points={expected.Points.Length} - VERIFIED");
            }

            _output.WriteLine("=== TEST PASSED ===");
        }
        finally
        {
            await StopPython(pythonProcess);
        }
    }

    [Fact]
    public async Task PythonMultipleFrames_KeyPointsDecoder_HandlesAllFrames()
    {
        // Skip if not on Linux/macOS
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            _output.WriteLine("Skipping test - Unix sockets not supported on this platform");
            return;
        }

        if (!File.Exists(_pythonVenvPath) || !File.Exists(_pythonScriptPath))
        {
            _output.WriteLine("Python environment not available, skipping test");
            return;
        }

        const int frameCount = 3;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        _output.WriteLine($"=== Python Multiple Frames Test ({frameCount} frames) ===");

        var pythonProcess = StartPythonWriter(frameCount);

        try
        {
            await WaitForSocketFile(_keypointsSocketPath, TimeSpan.FromSeconds(10), cts.Token);

            using var source = await UnixSocketFrameSource.ConnectAsync(
                _keypointsSocketPath,
                TimeSpan.FromSeconds(5),
                retry: true);

            var receivedFrames = new List<ulong>();

            // Single decoder instance to maintain delta state across frames
            var canvas = new MockCanvas();
            var stage = new MockStage(canvas);
            var decoder = new KeyPointsDecoder(stage);

            for (int i = 0; i < frameCount; i++)
            {
                var frameData = await source.ReadFrameAsync(cts.Token);
                if (frameData.IsEmpty) break;

                canvas.Clear();  // Clear previous draw calls
                var result = decoder.Decode(frameData.Span);
                Assert.True(result.Success, $"Frame {i + 1} decode failed");
                Assert.NotNull(result.FrameId);
                receivedFrames.Add(result.FrameId!.Value);

                _output.WriteLine($"Frame {i + 1}: ID={result.FrameId}, Lines={canvas.LineCalls.Count}");
            }

            Assert.Equal(frameCount, receivedFrames.Count);
            _output.WriteLine($"Successfully received and decoded {frameCount} frames");
            _output.WriteLine("=== TEST PASSED ===");
        }
        finally
        {
            await StopPython(pythonProcess);
        }
    }

    [Fact]
    public async Task PythonKeyPointsAndSegmentation_BothDecodersWork_ConcurrentSockets()
    {
        // Skip if not on Linux/macOS
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            _output.WriteLine("Skipping test - Unix sockets not supported on this platform");
            return;
        }

        if (!File.Exists(_pythonVenvPath) || !File.Exists(_pythonScriptPath))
        {
            _output.WriteLine("Python environment not available, skipping test");
            return;
        }

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        _output.WriteLine("=== Python Concurrent Sockets Test ===");

        var pythonProcess = StartPythonWriter(frameCount: 1);

        try
        {
            // Wait for both sockets
            var kpWait = WaitForSocketFile(_keypointsSocketPath, TimeSpan.FromSeconds(10), cts.Token);
            var segWait = WaitForSocketFile(_segmentationSocketPath, TimeSpan.FromSeconds(10), cts.Token);
            await Task.WhenAll(kpWait, segWait);

            _output.WriteLine("Both sockets ready, connecting...");

            // Connect to both sockets concurrently
            var kpSourceTask = UnixSocketFrameSource.ConnectAsync(_keypointsSocketPath, TimeSpan.FromSeconds(5), retry: true);
            var segSourceTask = UnixSocketFrameSource.ConnectAsync(_segmentationSocketPath, TimeSpan.FromSeconds(5), retry: true);

            using var kpSource = await kpSourceTask;
            using var segSource = await segSourceTask;

            _output.WriteLine("Connected to both sockets");

            // Read from both concurrently
            var kpRead = kpSource.ReadFrameAsync(cts.Token);
            var segRead = segSource.ReadFrameAsync(cts.Token);

            var kpData = await kpRead;
            var segData = await segRead;

            Assert.False(kpData.IsEmpty);
            Assert.False(segData.IsEmpty);

            // Decode keypoints
            var kpCanvas = new MockCanvas();
            var kpStage = new MockStage(kpCanvas);
            var kpDecoder = new KeyPointsDecoder(kpStage);
            var kpResult = kpDecoder.Decode(kpData.Span);

            // Decode segmentation
            var segCanvas = new MockCanvas();
            var segStage = new MockStage(segCanvas);
            var segDecoder = new SegmentationDecoder(segStage);
            var segResult = segDecoder.Decode(segData.Span);

            // Verify both decoded successfully
            Assert.True(kpResult.Success, "KeyPoints decoder failed");
            Assert.True(segResult.Success, "Segmentation decoder failed");
            Assert.Equal(ExpectedFrameId, kpResult.FrameId);
            Assert.Equal(ExpectedFrameId, segResult.FrameId);

            _output.WriteLine($"KeyPoints: FrameId={kpResult.FrameId}, Crosses={kpCanvas.LineCalls.Count / 2}");
            _output.WriteLine($"Segmentation: FrameId={segResult.FrameId}, Polygons={segCanvas.PolygonCalls.Count}");

            Assert.Equal(ExpectedKeyPoints.Length * 2, kpCanvas.LineCalls.Count);
            Assert.Equal(ExpectedSegmentationInstances.Length, segCanvas.PolygonCalls.Count);

            _output.WriteLine("=== TEST PASSED ===");
        }
        finally
        {
            await StopPython(pythonProcess);
        }
    }

    private Process StartPythonWriter(int frameCount)
    {
        var args = $"\"{_pythonScriptPath}\" \"{_keypointsSocketPath}\" \"{_segmentationSocketPath}\" --frames {frameCount}";

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

    private async Task WaitForSocketFile(string socketPath, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!File.Exists(socketPath) && DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(50, ct);
        }

        if (!File.Exists(socketPath))
            throw new TimeoutException($"Socket file not created: {socketPath}");
    }

    private async Task StopPython(Process process)
    {
        if (process.HasExited) return;

        try
        {
            // Give it a moment to finish naturally
            process.WaitForExit(1000);

            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(2000);
            }

            // Log output
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();

            if (!string.IsNullOrWhiteSpace(stdout))
                _output.WriteLine($"Python stdout:\n{stdout}");
            if (!string.IsNullOrWhiteSpace(stderr))
                _output.WriteLine($"Python stderr:\n{stderr}");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Error stopping Python: {ex.Message}");
        }
    }
}
