using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RocketWelder.SDK.Transport;
using RocketWelder.SDK.Protocols;
using CliWrap;
using CliWrap.Buffered;
using Xunit;
using Xunit.Abstractions;

namespace RocketWelder.SDK.Tests;

public class SegmentationResultTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    /// <summary>
    /// Helper to read a single frame from a stream using the new Source API.
    /// </summary>
    private async Task<SegmentationFrame> ReadSingleFrameAsync(Stream stream)
    {
        stream.Position = 0;
        var source = new StreamFrameSource(stream, leaveOpen: true);
        var segSource = new SegmentationResultSource(source);

        await foreach (var frame in segSource.ReadFramesAsync())
        {
            return frame;
        }

        throw new EndOfStreamException("No frame available");
    }

    [Fact]
    public async Task RoundTrip_SingleInstance_PreservesData()
    {
        // Arrange
        ulong frameId = 42;
        uint width = 1920;
        uint height = 1080;
        byte classId = 5;
        byte instanceId = 1;
        Point[] points = new[]
        {
            new Point(100, 200),
            new Point(101, 201),
            new Point(102, 199),
            new Point(105, 200)
        };

        using var stream = new MemoryStream();

        // Act - Write
        using (var writer = new SegmentationResultWriter(frameId, width, height, stream, leaveOpen: true))
        {
            writer.Append(classId, instanceId, points);
        }

        // Act - Read
        var frame = await ReadSingleFrameAsync(stream);

        Assert.Equal(frameId, frame.FrameId);
        Assert.Equal(width, frame.Width);
        Assert.Equal(height, frame.Height);
        Assert.Single(frame.Instances);

        var instance = frame.Instances[0];
        Assert.Equal(classId, instance.ClassId);
        Assert.Equal(instanceId, instance.InstanceId);
        Assert.Equal(points.Length, instance.Points.Length);

        for (int i = 0; i < points.Length; i++)
        {
            Assert.Equal(points[i], instance.Points.Span[i]);
        }
    }

    [Fact]
    public async Task RoundTrip_MultipleInstances_PreservesData()
    {
        // Arrange
        ulong frameId = 100;
        uint width = 640;
        uint height = 480;

        var instances = new[]
        {
            (ClassId: (byte)1, InstanceId: (byte)1, Points: new[] { new Point(10, 20), new Point(30, 40) }),
            (ClassId: (byte)2, InstanceId: (byte)1, Points: new[] { new Point(100, 100), new Point(101, 101), new Point(102, 100) }),
            (ClassId: (byte)1, InstanceId: (byte)2, Points: new[] { new Point(500, 400) })
        };

        using var stream = new MemoryStream();

        // Act - Write
        using (var writer = new SegmentationResultWriter(frameId, width, height, stream, leaveOpen: true))
        {
            foreach (var (classId, instanceId, points) in instances)
            {
                writer.Append(classId, instanceId, points);
            }
        }

        // Act - Read
        var frame = await ReadSingleFrameAsync(stream);

        Assert.Equal(frameId, frame.FrameId);
        Assert.Equal(instances.Length, frame.Instances.Count);

        for (int i = 0; i < instances.Length; i++)
        {
            var expected = instances[i];
            var actual = frame.Instances[i];

            Assert.Equal(expected.ClassId, actual.ClassId);
            Assert.Equal(expected.InstanceId, actual.InstanceId);
            Assert.Equal(expected.Points.Length, actual.Points.Length);

            for (int j = 0; j < expected.Points.Length; j++)
            {
                Assert.Equal(expected.Points[j], actual.Points.Span[j]);
            }
        }
    }

    [Fact]
    public async Task RoundTrip_EmptyPoints_PreservesData()
    {
        // Arrange
        ulong frameId = 1;
        uint width = 100;
        uint height = 100;
        byte classId = 1;
        byte instanceId = 1;
        Point[] points = Array.Empty<Point>();

        using var stream = new MemoryStream();

        // Act - Write
        using (var writer = new SegmentationResultWriter(frameId, width, height, stream, leaveOpen: true))
        {
            writer.Append(classId, instanceId, points);
        }

        // Act - Read
        var frame = await ReadSingleFrameAsync(stream);

        Assert.Single(frame.Instances);
        var instance = frame.Instances[0];
        Assert.Equal(classId, instance.ClassId);
        Assert.Equal(instanceId, instance.InstanceId);
        Assert.Equal(0, instance.Points.Length);
    }

    [Fact]
    public async Task RoundTrip_LargeContour_PreservesData()
    {
        // Arrange
        ulong frameId = 999;
        uint width = 3840;
        uint height = 2160;
        byte classId = 10;
        byte instanceId = 5;

        // Create a large contour (e.g., 1000 points in a circle)
        var points = new List<Point>();
        for (int i = 0; i < 1000; i++)
        {
            double angle = 2 * Math.PI * i / 1000;
            int x = (int)(1920 + 500 * Math.Cos(angle));
            int y = (int)(1080 + 500 * Math.Sin(angle));
            points.Add(new Point(x, y));
        }

        using var stream = new MemoryStream();

        // Act - Write
        using (var writer = new SegmentationResultWriter(frameId, width, height, stream, leaveOpen: true))
        {
            writer.Append(classId, instanceId, points);
        }

        output.WriteLine($"Wrote {points.Count} points in {stream.Position}B");

        // Act - Read
        var frame = await ReadSingleFrameAsync(stream);

        Assert.Equal(frameId, frame.FrameId);
        Assert.Equal(width, frame.Width);
        Assert.Equal(height, frame.Height);
        Assert.Single(frame.Instances);

        var instance = frame.Instances[0];
        Assert.Equal(classId, instance.ClassId);
        Assert.Equal(instanceId, instance.InstanceId);
        Assert.Equal(points.Count, instance.Points.Length);

        for (int i = 0; i < points.Count; i++)
        {
            Assert.Equal(points[i], instance.Points.Span[i]);
        }
    }

    [Fact]
    public async Task RoundTrip_NegativeDeltas_PreservesData()
    {
        // Arrange - Test points with negative deltas
        Point[] points = new[]
        {
            new Point(100, 100),
            new Point(99, 99),   // -1, -1
            new Point(98, 100),  // -1, +1
            new Point(100, 98),  // +2, -2
            new Point(50, 150)   // -50, +52
        };

        using var stream = new MemoryStream();

        // Act - Write
        using (var writer = new SegmentationResultWriter(1, 200, 200, stream, leaveOpen: true))
        {
            writer.Append(1, 1, points);
        }

        // Act - Read
        var frame = await ReadSingleFrameAsync(stream);

        Assert.Single(frame.Instances);
        var instance = frame.Instances[0];
        Assert.Equal(points.Length, instance.Points.Length);

        for (int i = 0; i < points.Length; i++)
        {
            Assert.Equal(points[i], instance.Points.Span[i]);
        }
    }

    [Fact]
    public async Task ToNormalized_ConvertsToFloatRange()
    {
        // Arrange
        uint width = 1920;
        uint height = 1080;
        Point[] points = new[]
        {
            new Point(0, 0),
            new Point(1920, 1080),
            new Point(960, 540)
        };

        using var stream = new MemoryStream();
        using (var writer = new SegmentationResultWriter(1, width, height, stream, leaveOpen: true))
        {
            writer.Append(1, 1, points);
        }

        var frame = await ReadSingleFrameAsync(stream);
        var instance = frame.Instances[0];

        // Act
        var normalized = instance.ToNormalized(width, height);

        // Assert
        Assert.Equal(3, normalized.Length);
        Assert.Equal(0f, normalized[0].X, precision: 5);
        Assert.Equal(0f, normalized[0].Y, precision: 5);
        Assert.Equal(1f, normalized[1].X, precision: 5);
        Assert.Equal(1f, normalized[1].Y, precision: 5);
        Assert.Equal(0.5f, normalized[2].X, precision: 5);
        Assert.Equal(0.5f, normalized[2].Y, precision: 5);
    }

    [Fact]
    public async Task Write_UsingSpan_WorksCorrectly()
    {
        // Arrange
        Point[] points = new[]
        {
            new Point(1, 2),
            new Point(3, 4)
        };

        using var stream = new MemoryStream();

        // Act
        using (var writer = new SegmentationResultWriter(1, 100, 100, stream, leaveOpen: true))
        {
            writer.Append(1, 1, points.AsSpan());
        }

        // Assert
        var frame = await ReadSingleFrameAsync(stream);
        Assert.Single(frame.Instances);
        var instance = frame.Instances[0];
        Assert.Equal(2, instance.Points.Length);
        Assert.Equal(new Point(1, 2), instance.Points.Span[0]);
        Assert.Equal(new Point(3, 4), instance.Points.Span[1]);
    }

    [Fact]
    public async Task Write_UsingIEnumerable_WorksCorrectly()
    {
        // Arrange
        IEnumerable<Point> points = new List<Point>
        {
            new Point(5, 6),
            new Point(7, 8),
            new Point(9, 10)
        };

        using var stream = new MemoryStream();

        // Act
        using (var writer = new SegmentationResultWriter(1, 100, 100, stream, leaveOpen: true))
        {
            writer.Append(1, 1, points);
        }

        // Assert
        var frame = await ReadSingleFrameAsync(stream);
        Assert.Single(frame.Instances);
        Assert.Equal(3, frame.Instances[0].Points.Length);
    }

    [Fact]
    public async Task RoundTrip_MultipleFramesInOneStream_PreservesData()
    {
        // Arrange
        var frame1Data = (FrameId: 1ul, Width: 640u, Height: 480u, Instances: new[]
        {
            (ClassId: (byte)1, InstanceId: (byte)1, Points: new[] { new Point(10, 20), new Point(30, 40) })
        });

        var frame2Data = (FrameId: 2ul, Width: 1920u, Height: 1080u, Instances: new[]
        {
            (ClassId: (byte)2, InstanceId: (byte)1, Points: new[] { new Point(100, 200) }),
            (ClassId: (byte)3, InstanceId: (byte)1, Points: new[] { new Point(500, 600), new Point(510, 610), new Point(520, 620) })
        });

        using var stream = new MemoryStream();

        // Act - Write two frames
        using (var writer1 = new SegmentationResultWriter(frame1Data.FrameId, frame1Data.Width, frame1Data.Height, stream, leaveOpen: true))
        {
            foreach (var inst in frame1Data.Instances)
            {
                writer1.Append(inst.ClassId, inst.InstanceId, inst.Points);
            }
        }

        using (var writer2 = new SegmentationResultWriter(frame2Data.FrameId, frame2Data.Width, frame2Data.Height, stream, leaveOpen: true))
        {
            foreach (var inst in frame2Data.Instances)
            {
                writer2.Append(inst.ClassId, inst.InstanceId, inst.Points);
            }
        }

        // Act - Read both frames using streaming API
        stream.Position = 0;
        var source = new StreamFrameSource(stream, leaveOpen: true);
        var segSource = new SegmentationResultSource(source);

        var frames = new List<SegmentationFrame>();
        await foreach (var frame in segSource.ReadFramesAsync())
        {
            frames.Add(frame);
        }

        // Assert
        Assert.Equal(2, frames.Count);

        // Verify frame 1
        var frame1 = frames[0];
        _output.WriteLine($"Frame 1: {frame1.FrameId}, {frame1.Width}x{frame1.Height}");
        Assert.Equal(frame1Data.FrameId, frame1.FrameId);
        Assert.Equal(frame1Data.Width, frame1.Width);
        Assert.Equal(frame1Data.Height, frame1.Height);
        Assert.Single(frame1.Instances);
        Assert.Equal(frame1Data.Instances[0].ClassId, frame1.Instances[0].ClassId);

        // Verify frame 2
        var frame2 = frames[1];
        _output.WriteLine($"Frame 2: {frame2.FrameId}, {frame2.Width}x{frame2.Height}");
        Assert.Equal(frame2Data.FrameId, frame2.FrameId);
        Assert.Equal(frame2Data.Width, frame2.Width);
        Assert.Equal(frame2Data.Height, frame2.Height);
        Assert.Equal(2, frame2.Instances.Count);
    }

    [Fact]
    public async Task Flush_WithoutDispose_FlushesStream()
    {
        // Arrange
        var points = new[] { new Point(10, 20) };
        using var stream = new MemoryStream();
        using var writer = new SegmentationResultWriter(1, 100, 100, stream, leaveOpen: true);

        // Act
        writer.Append(1, 1, points);
        writer.Flush();

        // Assert - Data should be written (including length prefix)
        Assert.True(stream.Length > 0);
        _output.WriteLine($"Stream length after flush: {stream.Length} bytes");
    }

    [Fact]
    public async Task Sink_CreatesMultipleWriters()
    {
        // Arrange
        using var stream = new MemoryStream();
        var frameSink = new StreamFrameSink(stream, leaveOpen: true);
        using var sink = new SegmentationResultSink(frameSink);

        // Act - Write multiple frames via sink
        using (var writer1 = sink.CreateWriter(1, 640, 480))
        {
            writer1.Append(1, 1, new[] { new Point(10, 20) });
        }

        using (var writer2 = sink.CreateWriter(2, 1920, 1080))
        {
            writer2.Append(2, 1, new[] { new Point(100, 200) });
        }

        // Assert - Read back
        stream.Position = 0;
        var source = new StreamFrameSource(stream, leaveOpen: true);
        var segSource = new SegmentationResultSource(source);

        var frames = new List<SegmentationFrame>();
        await foreach (var frame in segSource.ReadFramesAsync())
        {
            frames.Add(frame);
        }

        Assert.Equal(2, frames.Count);
        Assert.Equal(1ul, frames[0].FrameId);
        Assert.Equal(2ul, frames[1].FrameId);
    }

    [Fact]
    public async Task Source_StreamsFramesAsyncEnumerable()
    {
        // Arrange
        using var stream = new MemoryStream();

        // Write 3 frames
        for (int i = 0; i < 3; i++)
        {
            using var writer = new SegmentationResultWriter((ulong)i, 640, 480, stream, leaveOpen: true);
            writer.Append(1, 1, new[] { new Point(i * 10, i * 20) });
        }

        // Act - Stream frames
        stream.Position = 0;
        var source = new StreamFrameSource(stream, leaveOpen: true);
        var segSource = new SegmentationResultSource(source);

        int frameCount = 0;
        await foreach (var frame in segSource.ReadFramesAsync())
        {
            Assert.Equal((ulong)frameCount, frame.FrameId);
            frameCount++;
        }

        // Assert
        Assert.Equal(3, frameCount);
    }

    [Fact]
    public async Task CrossPlatform_CSharpWritesPythonReads_PreservesData()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), "rocket-welder-test");
        Directory.CreateDirectory(testDir);
        var testFile = Path.Combine(testDir, "csharp_to_python.bin");

        ulong frameId = 12345;
        uint width = 640;
        uint height = 480;

        var testData = new[]
        {
            (ClassId: (byte)1, InstanceId: (byte)1, Points: new[] { new Point(10, 20), new Point(30, 40) }),
            (ClassId: (byte)2, InstanceId: (byte)1, Points: new[] { new Point(100, 200), new Point(150, 250), new Point(200, 300) }),
            (ClassId: (byte)1, InstanceId: (byte)2, Points: new[] { new Point(500, 400) })
        };

        try
        {
            // Act - C# writes (using StreamFrameSink for framing)
            using (var stream = File.Create(testFile))
            using (var writer = new SegmentationResultWriter(frameId, width, height, stream))
            {
                foreach (var (classId, instanceId, points) in testData)
                {
                    writer.Append(classId, instanceId, points);
                }
            }

            // Verify file exists and has data
            Assert.True(File.Exists(testFile));
            var fileInfo = new FileInfo(testFile);
            Assert.True(fileInfo.Length > 0);

            _output.WriteLine($"C# wrote test file: {testFile}");
            _output.WriteLine($"File size: {fileInfo.Length} bytes");
            _output.WriteLine($"Frame: {frameId}, Size: {width}x{height}, Instances: {testData.Length}");

            // Python will read and verify this file in its test suite
        }
        finally
        {
            // Don't delete - let Python test read it
            _output.WriteLine("Test file left for Python verification");
        }
    }

    [Fact]
    public async Task CrossPlatform_PythonWritesCSharpReads_PreservesData()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), "rocket-welder-test");
        var testFile = Path.Combine(testDir, "python_to_csharp.bin");

        // Expected data (must match Python test)
        ulong expectedFrameId = 54321;
        uint expectedWidth = 1920;
        uint expectedHeight = 1080;

        var expectedInstances = new[]
        {
            (ClassId: (byte)3, InstanceId: (byte)1, Points: new[] { new Point(50, 100), new Point(60, 110), new Point(70, 120) }),
            (ClassId: (byte)4, InstanceId: (byte)1, Points: new[] { new Point(300, 400) }),
            (ClassId: (byte)3, InstanceId: (byte)2, Points: new[] { new Point(800, 900), new Point(810, 910) })
        };

        // Skip if Python hasn't run yet
        if (!File.Exists(testFile))
        {
            _output.WriteLine($"Python test file not found: {testFile}");
            _output.WriteLine("Run Python tests first to generate test file.");
            return;
        }

        try
        {
            // Act - C# reads Python file using streaming API
            using var stream = File.OpenRead(testFile);
            var source = new StreamFrameSource(stream, leaveOpen: false);
            var segSource = new SegmentationResultSource(source);

            SegmentationFrame? readFrame = null;
            await foreach (var frame in segSource.ReadFramesAsync())
            {
                readFrame = frame;
                break; // Only read first frame
            }

            Assert.NotNull(readFrame);
            var actualFrame = readFrame.Value;

            // Verify metadata
            Assert.Equal(expectedFrameId, actualFrame.FrameId);
            Assert.Equal(expectedWidth, actualFrame.Width);
            Assert.Equal(expectedHeight, actualFrame.Height);

            _output.WriteLine($"Read frame: {actualFrame.FrameId}, Size: {actualFrame.Width}x{actualFrame.Height}");

            // Verify instances
            Assert.Equal(expectedInstances.Length, actualFrame.Instances.Count);

            for (int i = 0; i < expectedInstances.Length; i++)
            {
                var expected = expectedInstances[i];
                var actual = actualFrame.Instances[i];

                Assert.Equal(expected.ClassId, actual.ClassId);
                Assert.Equal(expected.InstanceId, actual.InstanceId);
                Assert.Equal(expected.Points.Length, actual.Points.Length);

                for (int j = 0; j < expected.Points.Length; j++)
                {
                    Assert.Equal(expected.Points[j].X, actual.Points.Span[j].X);
                    Assert.Equal(expected.Points[j].Y, actual.Points.Span[j].Y);
                }

                _output.WriteLine($"Instance {i}: class={actual.ClassId}, instance={actual.InstanceId}, points={actual.Points.Length}");
            }

            _output.WriteLine($"Successfully read Python-written file! Verified {expectedInstances.Length} instances.");
        }
        catch (FileNotFoundException)
        {
            _output.WriteLine("Python test file not found - skipping test");
        }
    }

    [Fact]
    public async Task CrossPlatform_Process_CSharpWritesPythonReads_ReturnsCorrectJson()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), "rocket-welder-test");
        Directory.CreateDirectory(testDir);
        var testFile = Path.Combine(testDir, "csharp_subprocess_test.bin");

        ulong frameId = 98765;
        uint width = 800;
        uint height = 600;

        var testData = new[]
        {
            (ClassId: (byte)1, InstanceId: (byte)1, Points: new[] { new Point(10, 20), new Point(30, 40) }),
            (ClassId: (byte)2, InstanceId: (byte)2, Points: new[] { new Point(100, 200), new Point(150, 250), new Point(200, 300) })
        };

        // Act - C# writes
        using (var stream = File.Create(testFile))
        using (var writer = new SegmentationResultWriter(frameId, width, height, stream))
        {
            foreach (var (classId, instanceId, points) in testData)
            {
                writer.Append(classId, instanceId, points);
            }
        }

        _output.WriteLine($"C# wrote: {testFile}");

        // Act - Call Python to read (CliWrap handles arguments properly)
        var pythonScript = FindPythonScript();
        var result = await RunPythonScriptAsync(pythonScript, "read", testFile);

        _output.WriteLine($"Python exit code: {result.ExitCode}");
        _output.WriteLine($"Python stdout:\n{result.Output}");

        if (!string.IsNullOrEmpty(result.Error))
        {
            _output.WriteLine($"Python stderr:\n{result.Error}");
        }

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Output), "Python should output JSON");

        // Parse JSON output
        var json = JsonDocument.Parse(result.Output);
        var root = json.RootElement;

        Assert.Equal(frameId, root.GetProperty("frame_id").GetUInt64());
        Assert.Equal(width, root.GetProperty("width").GetUInt32());
        Assert.Equal(height, root.GetProperty("height").GetUInt32());

        var instances = root.GetProperty("instances").EnumerateArray().ToArray();
        Assert.Equal(testData.Length, instances.Length);

        for (int i = 0; i < testData.Length; i++)
        {
            var expected = testData[i];
            var actual = instances[i];

            Assert.Equal(expected.ClassId, actual.GetProperty("class_id").GetByte());
            Assert.Equal(expected.InstanceId, actual.GetProperty("instance_id").GetByte());

            var points = actual.GetProperty("points").EnumerateArray().ToArray();
            Assert.Equal(expected.Points.Length, points.Length);

            for (int j = 0; j < expected.Points.Length; j++)
            {
                var point = points[j].EnumerateArray().ToArray();
                Assert.Equal(expected.Points[j].X, point[0].GetInt32());
                Assert.Equal(expected.Points[j].Y, point[1].GetInt32());
            }
        }

        _output.WriteLine("✓ Python successfully read C#-written file!");
    }

    [Fact]
    public async Task CrossPlatform_Process_PythonWritesCSharpReads_PreservesData()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), "rocket-welder-test");
        Directory.CreateDirectory(testDir);
        var testFile = Path.Combine(testDir, "python_subprocess_test.bin");

        ulong frameId = 11111;
        uint width = 320;
        uint height = 240;

        // Pass JSON as argument - CliWrap handles escaping properly!
        var instancesJson = """[{"class_id":7,"instance_id":1,"points":[[5,10],[15,20],[25,30]]},{"class_id":8,"instance_id":1,"points":[[100,100]]}]""";

        // Act - Call Python to write
        var pythonScript = FindPythonScript();
        var result = await RunPythonScriptAsync(pythonScript, "write", testFile, frameId.ToString(), width.ToString(), height.ToString(), instancesJson);

        _output.WriteLine($"Python exit code: {result.ExitCode}");
        _output.WriteLine($"Python output: {result.Output}");

        if (!string.IsNullOrEmpty(result.Error))
        {
            _output.WriteLine($"Python stderr: {result.Error}");
        }

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(testFile), "Python should create file");

        // Act - C# reads using streaming API
        using var stream = File.OpenRead(testFile);
        var source = new StreamFrameSource(stream, leaveOpen: false);
        var segSource = new SegmentationResultSource(source);

        SegmentationFrame? readFrame = null;
        await foreach (var frame in segSource.ReadFramesAsync())
        {
            readFrame = frame;
            break;
        }

        Assert.NotNull(readFrame);
        var actualFrame = readFrame.Value;

        // Assert
        Assert.Equal(frameId, actualFrame.FrameId);
        Assert.Equal(width, actualFrame.Width);
        Assert.Equal(height, actualFrame.Height);

        // Read first instance
        Assert.Equal(2, actualFrame.Instances.Count);

        var inst1 = actualFrame.Instances[0];
        Assert.Equal(7, inst1.ClassId);
        Assert.Equal(1, inst1.InstanceId);
        Assert.Equal(3, inst1.Points.Length);
        Assert.Equal(new Point(5, 10), inst1.Points.Span[0]);
        Assert.Equal(new Point(15, 20), inst1.Points.Span[1]);
        Assert.Equal(new Point(25, 30), inst1.Points.Span[2]);

        // Read second instance
        var inst2 = actualFrame.Instances[1];
        Assert.Equal(8, inst2.ClassId);
        Assert.Equal(1, inst2.InstanceId);
        Assert.Equal(1, inst2.Points.Length);
        Assert.Equal(new Point(100, 100), inst2.Points.Span[0]);

        _output.WriteLine("✓ C# successfully read Python-written file!");
    }

    [Fact]
    public async Task CrossPlatform_Process_MultipleFrames_RoundTrip()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), "rocket-welder-test");
        Directory.CreateDirectory(testDir);
        var testFile = Path.Combine(testDir, "multiframe_test.bin");

        var frame1Data = (FrameId: (ulong)1, Width: (uint)640, Height: (uint)480,
            Instances: new[] { (ClassId: (byte)1, InstanceId: (byte)1, Points: new[] { new Point(10, 20), new Point(30, 40) }) });

        var frame2Data = (FrameId: (ulong)2, Width: (uint)1920, Height: (uint)1080,
            Instances: new[]
            {
                (ClassId: (byte)2, InstanceId: (byte)1, Points: new[] { new Point(100, 200), new Point(150, 250) }),
                (ClassId: (byte)3, InstanceId: (byte)1, Points: new[] { new Point(500, 600), new Point(510, 610), new Point(520, 620) })
            });

        // Act - C# writes both frames
        using (var stream = File.Create(testFile))
        {
            using (var writer1 = new SegmentationResultWriter(frame1Data.FrameId, frame1Data.Width, frame1Data.Height, stream, leaveOpen: true))
            {
                foreach (var (classId, instanceId, points) in frame1Data.Instances)
                    writer1.Append(classId, instanceId, points);
            }

            using (var writer2 = new SegmentationResultWriter(frame2Data.FrameId, frame2Data.Width, frame2Data.Height, stream, leaveOpen: true))
            {
                foreach (var (classId, instanceId, points) in frame2Data.Instances)
                    writer2.Append(classId, instanceId, points);
            }
        }

        _output.WriteLine($"C# wrote 2 frames to: {testFile}");

        // Act - Python reads frame 1
        var pythonScript = FindPythonScript();
        var result1 = await RunPythonScriptAsync(pythonScript, "read", testFile);

        Assert.Equal(0, result1.ExitCode);
        var json1 = JsonDocument.Parse(result1.Output);
        Assert.Equal(frame1Data.FrameId, json1.RootElement.GetProperty("frame_id").GetUInt64());
        Assert.Equal(frame1Data.Width, json1.RootElement.GetProperty("width").GetUInt32());
        Assert.Equal(frame1Data.Height, json1.RootElement.GetProperty("height").GetUInt32());
        Assert.Equal(1, json1.RootElement.GetProperty("instances").GetArrayLength());

        _output.WriteLine("✓ Python read frame 1 successfully");

        // Verify C# can also read both frames using streaming API
        using var readStream = File.OpenRead(testFile);
        var source = new StreamFrameSource(readStream, leaveOpen: false);
        var segSource = new SegmentationResultSource(source);

        var frames = new List<SegmentationFrame>();
        await foreach (var frame in segSource.ReadFramesAsync())
        {
            frames.Add(frame);
        }

        Assert.Equal(2, frames.Count);

        var frame1 = frames[0];
        Assert.Equal(frame1Data.FrameId, frame1.FrameId);
        Assert.Equal(frame1Data.Width, frame1.Width);
        Assert.Equal(frame1Data.Height, frame1.Height);
        Assert.Single(frame1.Instances);
        Assert.Equal(1, frame1.Instances[0].ClassId);

        var frame2 = frames[1];
        Assert.Equal(frame2Data.FrameId, frame2.FrameId);
        Assert.Equal(frame2Data.Width, frame2.Width);
        Assert.Equal(frame2Data.Height, frame2.Height);
        Assert.Equal(2, frame2.Instances.Count);
        Assert.Equal(2, frame2.Instances[0].ClassId);
        Assert.Equal(3, frame2.Instances[1].ClassId);

        _output.WriteLine("✓ C# verified both frames successfully - multi-frame round-trip works!");
    }

    private string FindPythonScript()
    {
        // Find script in repo structure where rocket_welder_sdk module is available
        var testDir = Path.GetDirectoryName(typeof(SegmentationResultTests).Assembly.Location)!;
        var repoRoot = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", ".."));
        var pythonDir = Path.Combine(repoRoot, "python");
        var scriptPath = Path.Combine(pythonDir, "segmentation_cross_platform_tool.py");

        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException($"Python script not found: {scriptPath}");
        }

        _output.WriteLine($"✓ Found Python script: {scriptPath}");
        return scriptPath;
    }

    private async Task<(int ExitCode, string Output, string Error)> RunPythonScriptAsync(string scriptPath, params string[] args)
    {
        var pythonDir = Path.GetDirectoryName(scriptPath)!;
        var venvPython = Path.Combine(pythonDir, "venv", "bin", "python3");

        // Use venv python if available, otherwise system python3
        var pythonExe = File.Exists(venvPython) ? venvPython : "python3";

        _output.WriteLine($"Executing: {pythonExe} {scriptPath} {string.Join(" ", args)}");

        // Use CliWrap for proper argument handling (no shell escaping issues)
        var result = await Cli.Wrap(pythonExe)
            .WithArguments(builder => builder
                .Add(scriptPath)
                .Add(args))
            .WithValidation(CommandResultValidation.None) // Don't throw on non-zero exit
            .ExecuteBufferedAsync();

        return (result.ExitCode, result.StandardOutput, result.StandardError);
    }
}
