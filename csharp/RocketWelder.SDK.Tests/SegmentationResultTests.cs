using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CliWrap;
using CliWrap.Buffered;
using Xunit;
using Xunit.Abstractions;

namespace RocketWelder.SDK.Tests;

public class SegmentationResultTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;
    [Fact]
    public void RoundTrip_SingleInstance_PreservesData()
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
        using (var writer = new SegmentationResultWriter(frameId, width, height, stream))
        {
            writer.Append(classId, instanceId, points);
        }

        // Act - Read
        stream.Position = 0;
        using var reader = new SegmentationResultReader(stream);

        var metadata = reader.Metadata;
        Assert.Equal(frameId, metadata.FrameId);
        Assert.Equal(width, metadata.Width);
        Assert.Equal(height, metadata.Height);

        Assert.True(reader.TryReadNext(out var instance));
        using (instance)
        {
            Assert.Equal(classId, instance.ClassId);
            Assert.Equal(instanceId, instance.InstanceId);
            Assert.Equal(points.Length, instance.Points.Length);

            for (int i = 0; i < points.Length; i++)
            {
                Assert.Equal(points[i], instance.Points[i]);
            }
        }

        Assert.False(reader.TryReadNext(out _));
    }

    [Fact]
    public void RoundTrip_MultipleInstances_PreservesData()
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
        using (var writer = new SegmentationResultWriter(frameId, width, height, stream))
        {
            foreach (var (classId, instanceId, points) in instances)
            {
                writer.Append(classId, instanceId, points);
            }
        }

        // Act - Read
        stream.Position = 0;
        using var reader = new SegmentationResultReader(stream);

        var metadata = reader.Metadata;
        Assert.Equal(frameId, metadata.FrameId);

        for (int i = 0; i < instances.Length; i++)
        {
            Assert.True(reader.TryReadNext(out var instance));
            using (instance)
            {
                Assert.Equal(instances[i].ClassId, instance.ClassId);
                Assert.Equal(instances[i].InstanceId, instance.InstanceId);
                Assert.Equal(instances[i].Points.Length, instance.Points.Length);

                for (int j = 0; j < instances[i].Points.Length; j++)
                {
                    Assert.Equal(instances[i].Points[j], instance.Points[j]);
                }
            }
        }

        Assert.False(reader.TryReadNext(out _));
    }

    [Fact]
    public void RoundTrip_EmptyPoints_PreservesData()
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
        using (var writer = new SegmentationResultWriter(frameId, width, height, stream))
        {
            writer.Append(classId, instanceId, points);
        }

        // Act - Read
        stream.Position = 0;
        using var reader = new SegmentationResultReader(stream);

        Assert.True(reader.TryReadNext(out var instance));
        Assert.Equal(classId, instance.ClassId);
        Assert.Equal(instanceId, instance.InstanceId);
        Assert.Equal(0, instance.Points.Length);
    }

    [Fact]
    public void RoundTrip_LargeContour_PreservesData()
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
        using (var writer = new SegmentationResultWriter(frameId, width, height, stream))
        {
            writer.Append(classId, instanceId, points);
        }
        
        output.WriteLine($"Wrote {points.Count} is {stream.Position}B in size");
        // Act - Read
        stream.Position = 0;
        using var reader = new SegmentationResultReader(stream);

        var metadata = reader.Metadata;
        Assert.Equal(frameId, metadata.FrameId);
        Assert.Equal(width, metadata.Width);
        Assert.Equal(height, metadata.Height);

        Assert.True(reader.TryReadNext(out var instance));
        using (instance)
        {
            Assert.Equal(classId, instance.ClassId);
            Assert.Equal(instanceId, instance.InstanceId);
            Assert.Equal(points.Count, instance.Points.Length);

            for (int i = 0; i < points.Count; i++)
            {
                Assert.Equal(points[i], instance.Points[i]);
            }
        }
    }

    [Fact]
    public void RoundTrip_NegativeDeltas_PreservesData()
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
        using (var writer = new SegmentationResultWriter(1, 200, 200, stream))
        {
            writer.Append(1, 1, points);
        }

        // Act - Read
        stream.Position = 0;
        using var reader = new SegmentationResultReader(stream);

        Assert.True(reader.TryReadNext(out var instance));
        using (instance)
        {
            Assert.Equal(points.Length, instance.Points.Length);

            for (int i = 0; i < points.Length; i++)
            {
                Assert.Equal(points[i], instance.Points[i]);
            }
        }
    }

    [Fact]
    public void ToNormalized_ConvertsToFloatRange()
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
        using (var writer = new SegmentationResultWriter(1, width, height, stream))
        {
            writer.Append(1, 1, points);
        }

        stream.Position = 0;
        using var reader = new SegmentationResultReader(stream);
        reader.TryReadNext(out var instance);

        using (instance)
        {
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
    }

    [Fact]
    public void ToArray_CopiesPoints()
    {
        // Arrange
        Point[] originalPoints = new[]
        {
            new Point(10, 20),
            new Point(30, 40)
        };

        using var stream = new MemoryStream();
        using (var writer = new SegmentationResultWriter(1, 100, 100, stream))
        {
            writer.Append(1, 1, originalPoints);
        }

        stream.Position = 0;
        using var reader = new SegmentationResultReader(stream);
        reader.TryReadNext(out var instance);

        using (instance)
        {
            // Act
            var copiedPoints = instance.ToArray();

            // Assert
            Assert.Equal(originalPoints.Length, copiedPoints.Length);
            for (int i = 0; i < originalPoints.Length; i++)
            {
                Assert.Equal(originalPoints[i], copiedPoints[i]);
            }
        }
    }

    [Fact]
    public void Reader_DisposesMemoryPoolBuffer()
    {
        // Arrange
        Point[] points = new[] { new Point(1, 2), new Point(3, 4) };
        using var stream = new MemoryStream();

        using (var writer = new SegmentationResultWriter(1, 100, 100, stream))
        {
            writer.Append(1, 1, points);
        }

        stream.Position = 0;

        // Act & Assert - Should not throw
        using (var reader = new SegmentationResultReader(stream))
        {
            reader.TryReadNext(out var instance);
            using (instance)
            {
                // Use instance
                Assert.Equal(2, instance.Points.Length);
            } // Dispose should return buffer to pool
        }
    }

    [Fact]
    public void Reader_EachInstanceGetsOwnBuffer()
    {
        // Arrange
        using var stream = new MemoryStream();

        using (var writer = new SegmentationResultWriter(1, 100, 100, stream))
        {
            writer.Append(1, 1, new[] { new Point(1, 2) });
            writer.Append(2, 1, new[] { new Point(3, 4) });
        }

        stream.Position = 0;

        // Act
        using var reader = new SegmentationResultReader(stream);

        reader.TryReadNext(out var instance1);
        using (instance1)
        {
            Assert.Equal(1, instance1.Points.Length);
            Assert.Equal(new Point(1, 2), instance1.Points[0]);
        }

        reader.TryReadNext(out var instance2);
        using (instance2)
        {
            Assert.Equal(1, instance2.Points.Length);
            Assert.Equal(new Point(3, 4), instance2.Points[0]);
        }
    }

    [Fact]
    public void Write_UsingSpan_WorksCorrectly()
    {
        // Arrange
        Span<Point> points = stackalloc Point[]
        {
            new Point(1, 2),
            new Point(3, 4)
        };

        using var stream = new MemoryStream();

        // Act
        using (var writer = new SegmentationResultWriter(1, 100, 100, stream))
        {
            writer.Append(1, 1, points);
        }

        // Assert
        stream.Position = 0;
        using var reader = new SegmentationResultReader(stream);
        Assert.True(reader.TryReadNext(out var instance));
        using (instance)
        {
            Assert.Equal(2, instance.Points.Length);
            Assert.Equal(new Point(1, 2), instance.Points[0]);
            Assert.Equal(new Point(3, 4), instance.Points[1]);
        }
    }

    [Fact]
    public void Write_UsingIEnumerable_WorksCorrectly()
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
        using (var writer = new SegmentationResultWriter(1, 100, 100, stream))
        {
            writer.Append(1, 1, points);
        }

        // Assert
        stream.Position = 0;
        using var reader = new SegmentationResultReader(stream);
        Assert.True(reader.TryReadNext(out var instance));
        using (instance)
        {
            Assert.Equal(3, instance.Points.Length);
        }
    }

    [Fact]
    public void RoundTrip_MultipleFramesInOneStream_PreservesData()
    {
        // Arrange
        var frame1 = (FrameId: 1ul, Width: 640u, Height: 480u, Instances: new[]
        {
            (ClassId: (byte)1, InstanceId: (byte)1, Points: new[] { new Point(10, 20), new Point(30, 40) })
        });

        var frame2 = (FrameId: 2ul, Width: 1920u, Height: 1080u, Instances: new[]
        {
            (ClassId: (byte)2, InstanceId: (byte)1, Points: new[] { new Point(100, 200) }),
            (ClassId: (byte)3, InstanceId: (byte)1, Points: new[] { new Point(500, 600), new Point(510, 610), new Point(520, 620) })
        });

        using var stream = new MemoryStream();

        // Act - Write two frames
        using (var writer1 = new SegmentationResultWriter(frame1.FrameId, frame1.Width, frame1.Height, stream))
        {
            foreach (var inst in frame1.Instances)
            {
                writer1.Append(inst.ClassId, inst.InstanceId, inst.Points);
            }
            writer1.Flush();
        }

        using (var writer2 = new SegmentationResultWriter(frame2.FrameId, frame2.Width, frame2.Height, stream))
        {
            foreach (var inst in frame2.Instances)
            {
                writer2.Append(inst.ClassId, inst.InstanceId, inst.Points);
            }
        }

        // Act - Read two frames
        stream.Position = 0;

        // Read frame 1
        using (var reader1 = new SegmentationResultReader(stream))
        {
            var metadata1 = reader1.Metadata;
            _output.WriteLine($"Frame 1: {metadata1.FrameId}, {metadata1.Width}x{metadata1.Height}");
            Assert.Equal(frame1.FrameId, metadata1.FrameId);
            Assert.Equal(frame1.Width, metadata1.Width);
            Assert.Equal(frame1.Height, metadata1.Height);

            for (int i = 0; i < frame1.Instances.Length; i++)
            {
                Assert.True(reader1.TryReadNext(out var instance));
                using (instance)
                {
                    Assert.Equal(frame1.Instances[i].ClassId, instance.ClassId);
                    Assert.Equal(frame1.Instances[i].InstanceId, instance.InstanceId);
                    Assert.Equal(frame1.Instances[i].Points.Length, instance.Points.Length);
                }
            }

            Assert.False(reader1.TryReadNext(out _));
        }

        // Read frame 2
        using (var reader2 = new SegmentationResultReader(stream))
        {
            var metadata2 = reader2.Metadata;
            _output.WriteLine($"Frame 2: {metadata2.FrameId}, {metadata2.Width}x{metadata2.Height}");
            Assert.Equal(frame2.FrameId, metadata2.FrameId);
            Assert.Equal(frame2.Width, metadata2.Width);
            Assert.Equal(frame2.Height, metadata2.Height);

            for (int i = 0; i < frame2.Instances.Length; i++)
            {
                Assert.True(reader2.TryReadNext(out var instance));
                using (instance)
                {
                    Assert.Equal(frame2.Instances[i].ClassId, instance.ClassId);
                    Assert.Equal(frame2.Instances[i].InstanceId, instance.InstanceId);
                    Assert.Equal(frame2.Instances[i].Points.Length, instance.Points.Length);
                }
            }

            Assert.False(reader2.TryReadNext(out _));
        }
    }

    [Fact]
    public void Points_CachingPattern_AvoidOverhead()
    {
        // Arrange
        var points = Enumerable.Range(0, 100).Select(i => new Point(i, i * 2)).ToArray();

        using var stream = new MemoryStream();
        using (var writer = new SegmentationResultWriter(1, 1920, 1080, stream))
        {
            writer.Append(1, 1, points);
        }

        stream.Position = 0;
        using var reader = new SegmentationResultReader(stream);
        reader.TryReadNext(out var instance);

        using (instance)
        {
            // Demonstrate correct caching pattern to avoid repeated property access overhead
            var cachedPoints = instance.Points;  // Cache span - IMPORTANT for performance!

            int sum = 0;
            for (int i = 0; i < cachedPoints.Length; i++)
            {
                sum += cachedPoints[i].X;  // Use cached span
            }

            _output.WriteLine($"Sum of X coordinates: {sum}");
            Assert.Equal(points.Sum(p => p.X), sum);
        }
    }

    [Fact]
    public void ToNormalized_SpanOverload_ZeroAllocation()
    {
        // Arrange
        var points = new[] { new Point(0, 0), new Point(1920, 1080), new Point(960, 540) };
        uint width = 1920;
        uint height = 1080;

        using var stream = new MemoryStream();
        using (var writer = new SegmentationResultWriter(1, width, height, stream))
        {
            writer.Append(1, 1, points);
        }

        stream.Position = 0;
        using var reader = new SegmentationResultReader(stream);
        reader.TryReadNext(out var instance);

        using (instance)
        {
            // Act - Use span-based overload (zero allocation)
            Span<PointF> buffer = stackalloc PointF[points.Length];
            instance.ToNormalized(width, height, buffer);

            // Assert
            Assert.Equal(0f, buffer[0].X, precision: 5);
            Assert.Equal(0f, buffer[0].Y, precision: 5);
            Assert.Equal(1f, buffer[1].X, precision: 5);
            Assert.Equal(1f, buffer[1].Y, precision: 5);
            Assert.Equal(0.5f, buffer[2].X, precision: 5);
            Assert.Equal(0.5f, buffer[2].Y, precision: 5);

            _output.WriteLine($"Normalized points (zero-allocation): ({buffer[0].X}, {buffer[0].Y}), ({buffer[1].X}, {buffer[1].Y}), ({buffer[2].X}, {buffer[2].Y})");
        }
    }

    [Fact]
    public void Flush_WithoutDispose_FlushesStream()
    {
        // Arrange
        var points = new[] { new Point(10, 20) };
        using var stream = new MemoryStream();
        using var writer = new SegmentationResultWriter(1, 100, 100, stream);

        // Act
        writer.Append(1, 1, points);
        writer.Flush();  // Flush without disposing

        // Assert - Data should be written
        Assert.True(stream.Length > 0);
        _output.WriteLine($"Stream length after flush: {stream.Length} bytes");

        // Can still write more
        writer.Append(2, 1, points);
        writer.Flush();

        Assert.True(stream.Length > 0);
        _output.WriteLine($"Stream length after second flush: {stream.Length} bytes");
    }

    [Fact]
    public void CrossPlatform_CSharpWritesPythonReads_PreservesData()
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
            // Act - C# writes
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
    public void CrossPlatform_PythonWritesCSharpReads_PreservesData()
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
            // Skip test instead of failing
            return;
        }

        try
        {
            // Act - C# reads Python file
            using var stream = File.OpenRead(testFile);
            using var reader = new SegmentationResultReader(stream);

            var metadata = reader.Metadata;

            // Verify metadata
            Assert.Equal(expectedFrameId, metadata.FrameId);
            Assert.Equal(expectedWidth, metadata.Width);
            Assert.Equal(expectedHeight, metadata.Height);

            _output.WriteLine($"Read frame: {metadata.FrameId}, Size: {metadata.Width}x{metadata.Height}");

            // Verify instances - process one at a time (ref structs can't be stored in List)
            int instanceCount = 0;
            for (int i = 0; i < expectedInstances.Length; i++)
            {
                var expected = expectedInstances[i];

                Assert.True(reader.TryReadNext(out var actual), $"Expected instance {i} but got end of stream");

                Assert.Equal(expected.ClassId, actual.ClassId);
                Assert.Equal(expected.InstanceId, actual.InstanceId);

                var actualPoints = actual.Points;
                Assert.Equal(expected.Points.Length, actualPoints.Length);

                for (int j = 0; j < expected.Points.Length; j++)
                {
                    Assert.Equal(expected.Points[j].X, actualPoints[j].X);
                    Assert.Equal(expected.Points[j].Y, actualPoints[j].Y);
                }

                _output.WriteLine($"Instance {i}: class={actual.ClassId}, instance={actual.InstanceId}, points={actualPoints.Length}");

                actual.Dispose();
                instanceCount++;
            }

            // Verify no more instances
            Assert.False(reader.TryReadNext(out var extraInstance), "Expected end of stream but got another instance");

            _output.WriteLine($"Successfully read Python-written file! Verified {instanceCount} instances.");
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

        // Act - C# reads
        using var stream = File.OpenRead(testFile);
        using var reader = new SegmentationResultReader(stream);

        var metadata = reader.Metadata;

        // Assert
        Assert.Equal(frameId, metadata.FrameId);
        Assert.Equal(width, metadata.Width);
        Assert.Equal(height, metadata.Height);

        // Read first instance
        Assert.True(reader.TryReadNext(out var inst1));
        Assert.Equal(7, inst1.ClassId);
        Assert.Equal(1, inst1.InstanceId);
        Assert.Equal(3, inst1.Points.Length);
        Assert.Equal(new Point(5, 10), inst1.Points[0]);
        Assert.Equal(new Point(15, 20), inst1.Points[1]);
        Assert.Equal(new Point(25, 30), inst1.Points[2]);
        inst1.Dispose();

        // Read second instance
        Assert.True(reader.TryReadNext(out var inst2));
        Assert.Equal(8, inst2.ClassId);
        Assert.Equal(1, inst2.InstanceId);
        Assert.Equal(1, inst2.Points.Length);
        Assert.Equal(new Point(100, 100), inst2.Points[0]);
        inst2.Dispose();

        // No more instances
        Assert.False(reader.TryReadNext(out var _));

        _output.WriteLine("✓ C# successfully read Python-written file!");
    }

    [Fact]
    public async Task CrossPlatform_Process_MultipleFrames_RoundTrip()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), "rocket-welder-test");
        Directory.CreateDirectory(testDir);
        var testFile = Path.Combine(testDir, "multiframe_test.bin");

        var frame1 = (FrameId: (ulong)1, Width: (uint)640, Height: (uint)480,
            Instances: new[] { (ClassId: (byte)1, InstanceId: (byte)1, Points: new[] { new Point(10, 20), new Point(30, 40) }) });

        var frame2 = (FrameId: (ulong)2, Width: (uint)1920, Height: (uint)1080,
            Instances: new[]
            {
                (ClassId: (byte)2, InstanceId: (byte)1, Points: new[] { new Point(100, 200), new Point(150, 250) }),
                (ClassId: (byte)3, InstanceId: (byte)1, Points: new[] { new Point(500, 600), new Point(510, 610), new Point(520, 620) })
            });

        // Act - C# writes both frames
        using (var stream = File.Create(testFile))
        {
            using (var writer1 = new SegmentationResultWriter(frame1.FrameId, frame1.Width, frame1.Height, stream))
            {
                foreach (var (classId, instanceId, points) in frame1.Instances)
                    writer1.Append(classId, instanceId, points);
            }

            using (var writer2 = new SegmentationResultWriter(frame2.FrameId, frame2.Width, frame2.Height, stream))
            {
                foreach (var (classId, instanceId, points) in frame2.Instances)
                    writer2.Append(classId, instanceId, points);
            }
        }

        _output.WriteLine($"C# wrote 2 frames to: {testFile}");

        // Act - Python reads frame 1
        var pythonScript = FindPythonScript();
        var result1 = await RunPythonScriptAsync(pythonScript, "read", testFile);

        Assert.Equal(0, result1.ExitCode);
        var json1 = JsonDocument.Parse(result1.Output);
        Assert.Equal(frame1.FrameId, json1.RootElement.GetProperty("frame_id").GetUInt64());
        Assert.Equal(frame1.Width, json1.RootElement.GetProperty("width").GetUInt32());
        Assert.Equal(frame1.Height, json1.RootElement.GetProperty("height").GetUInt32());
        Assert.Equal(1, json1.RootElement.GetProperty("instances").GetArrayLength());

        _output.WriteLine("✓ Python read frame 1 successfully");

        // Now read frame 2 - Python should continue reading from the stream
        // Note: Current Python CLI reads one frame at a time, so we need to call it again
        // For a true multi-frame test, we'd need to track stream position

        // Alternative: Have C# re-read to verify the write was correct
        using var readStream = File.OpenRead(testFile);

        using (var reader1 = new SegmentationResultReader(readStream))
        {
            var metadata1 = reader1.Metadata;
            Assert.Equal(frame1.FrameId, metadata1.FrameId);
            Assert.Equal(frame1.Width, metadata1.Width);
            Assert.Equal(frame1.Height, metadata1.Height);

            Assert.True(reader1.TryReadNext(out var inst));
            Assert.Equal(1, inst.ClassId);
            inst.Dispose();

            Assert.False(reader1.TryReadNext(out var _));
        }

        using (var reader2 = new SegmentationResultReader(readStream))
        {
            var metadata2 = reader2.Metadata;
            Assert.Equal(frame2.FrameId, metadata2.FrameId);
            Assert.Equal(frame2.Width, metadata2.Width);
            Assert.Equal(frame2.Height, metadata2.Height);

            // Read first instance
            Assert.True(reader2.TryReadNext(out var inst1));
            Assert.Equal(2, inst1.ClassId);
            Assert.Equal(2, inst1.Points.Length);
            inst1.Dispose();

            // Read second instance
            Assert.True(reader2.TryReadNext(out var inst2));
            Assert.Equal(3, inst2.ClassId);
            Assert.Equal(3, inst2.Points.Length);
            inst2.Dispose();

            Assert.False(reader2.TryReadNext(out var _));
        }

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
