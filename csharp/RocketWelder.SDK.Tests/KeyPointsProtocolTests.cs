using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RocketWelder.SDK.Transport;
using Xunit;

namespace RocketWelder.SDK.Tests;

public class KeyPointsProtocolTests
{
    private const string TestDefinitionJson = @"{
  ""version"": ""1.0"",
  ""compute_module_name"": ""TestModel"",
  ""points"": {
    ""nose"": 0,
    ""left_eye"": 1,
    ""right_eye"": 2,
    ""left_shoulder"": 3,
    ""right_shoulder"": 4
  }
}";

    [Fact]
    public async Task SingleFrame_RoundTrip_PreservesData()
    {
        // Arrange
        var stream = new MemoryStream();
        var storage = new KeyPointsSink(stream);

        var expectedKeypoints = new[]
        {
            (id: 0, point: new Point(100, 200), confidence: 0.95f),
            (id: 1, point: new Point(120, 190), confidence: 0.92f),
            (id: 2, point: new Point(80, 190), confidence: 0.88f),
            (id: 3, point: new Point(150, 300), confidence: 1.0f),
            (id: 4, point: new Point(50, 300), confidence: 0.75f)
        };

        // Act - Write
        using (var writer = storage.CreateWriter(frameId: 1))
        {
            foreach (var (id, point, confidence) in expectedKeypoints)
            {
                writer.Append(id, point, confidence);
            }
        }

        // Act - Read
        stream.Position = 0;
        using var frameSource = new StreamFrameSource(stream);
        var series = await storage.Read(TestDefinitionJson, frameSource);

        // Assert
        Assert.Equal("1.0", series.Version);
        Assert.Equal("TestModel", series.ComputeModuleName);
        Assert.Equal(5, series.Points.Count);
        Assert.True(series.ContainsFrame(1));

        var frame = series.GetFrame(1);
        Assert.NotNull(frame);
        Assert.Equal(5, frame!.Count);

        foreach (var (id, expectedPoint, expectedConfidence) in expectedKeypoints)
        {
            Assert.True(frame.ContainsKey(id));
            var result = frame[id];
            Assert.Equal(expectedPoint, result.point);
            Assert.Equal(expectedConfidence, result.confidence, precision: 4); // 0.0001 precision due to ushort encoding
        }
    }

    [Fact]
    public async Task MultipleFrames_WithMasterDelta_RoundTrip()
    {
        // Arrange
        var stream = new MemoryStream();
        var storage = new KeyPointsSink(stream, masterFrameInterval: 2);

        // Frame 1 - Master
        var frame1 = new[]
        {
            (id: 0, point: new Point(100, 200), confidence: 0.95f),
            (id: 1, point: new Point(120, 190), confidence: 0.92f)
        };

        // Frame 2 - Delta (small changes)
        var frame2 = new[]
        {
            (id: 0, point: new Point(101, 201), confidence: 0.94f),
            (id: 1, point: new Point(121, 191), confidence: 0.93f)
        };

        // Frame 3 - Master (interval hit)
        var frame3 = new[]
        {
            (id: 0, point: new Point(105, 205), confidence: 0.96f),
            (id: 1, point: new Point(125, 195), confidence: 0.91f)
        };

        // Act - Write
        using (var writer1 = storage.CreateWriter(frameId: 0))
        {
            foreach (var (id, point, confidence) in frame1)
                writer1.Append(id, point, confidence);
        }

        using (var writer2 = storage.CreateWriter(frameId: 1))
        {
            foreach (var (id, point, confidence) in frame2)
                writer2.Append(id, point, confidence);
        }

        using (var writer3 = storage.CreateWriter(frameId: 2))
        {
            foreach (var (id, point, confidence) in frame3)
                writer3.Append(id, point, confidence);
        }

        // Act - Read
        stream.Position = 0;
        using var frameSource = new StreamFrameSource(stream);
        var series = await storage.Read(TestDefinitionJson, frameSource);

        // Assert
        Assert.Equal(3, series.FrameIds.Count);
        Assert.True(series.ContainsFrame(0));
        Assert.True(series.ContainsFrame(1));
        Assert.True(series.ContainsFrame(2));

        // Verify Frame 1
        var actualFrame1 = series.GetFrame(0)!;
        Assert.Equal(frame1[0].point, actualFrame1[0].point);
        Assert.Equal(frame1[0].confidence, actualFrame1[0].confidence, precision: 4);

        // Verify Frame 2 (delta decoded correctly)
        var actualFrame2 = series.GetFrame(1)!;
        Assert.Equal(frame2[0].point, actualFrame2[0].point);
        Assert.Equal(frame2[0].confidence, actualFrame2[0].confidence, precision: 4);

        // Verify Frame 3 (master frame)
        var actualFrame3 = series.GetFrame(2)!;
        Assert.Equal(frame3[0].point, actualFrame3[0].point);
        Assert.Equal(frame3[0].confidence, actualFrame3[0].confidence, precision: 4);
    }

    [Fact]
    public async Task GetKeyPointTrajectory_ById_ReturnsCorrectSequence()
    {
        // Arrange
        var stream = new MemoryStream();
        var storage = new KeyPointsSink(stream);

        // Write 3 frames with nose (keypointId=0) moving
        for (ulong frameId = 0; frameId < 3; frameId++)
        {
            using var writer = storage.CreateWriter(frameId);
            writer.Append(keypointId: 0, x: (int)(100 + frameId * 10), y: (int)(200 + frameId * 5), confidence: 0.95f);
            writer.Append(keypointId: 1, x: 150, y: 250, confidence: 0.90f); // Static point
        }

        stream.Position = 0;
        using var frameSource = new StreamFrameSource(stream);
        var series = await storage.Read(TestDefinitionJson, frameSource);

        // Act
        var noseTrajectory = series.GetKeyPointTrajectory(keypointId: 0).ToList();

        // Assert
        Assert.Equal(3, noseTrajectory.Count);
        Assert.Equal(100, noseTrajectory[0].point.X);
        Assert.Equal(200, noseTrajectory[0].point.Y);
        Assert.Equal(110, noseTrajectory[1].point.X);
        Assert.Equal(205, noseTrajectory[1].point.Y);
        Assert.Equal(120, noseTrajectory[2].point.X);
        Assert.Equal(210, noseTrajectory[2].point.Y);
    }

    [Fact]
    public async Task GetKeyPointTrajectory_ByName_ReturnsCorrectSequence()
    {
        // Arrange
        var stream = new MemoryStream();
        var storage = new KeyPointsSink(stream);

        for (ulong frameId = 0; frameId < 2; frameId++)
        {
            using var writer = storage.CreateWriter(frameId);
            writer.Append(keypointId: 0, x: (int)(100 + frameId * 10), y: 200, confidence: 0.95f); // nose
            writer.Append(keypointId: 1, x: 150, y: 190, confidence: 0.90f); // left_eye
        }

        stream.Position = 0;
        using var frameSource = new StreamFrameSource(stream);
        var series = await storage.Read(TestDefinitionJson, frameSource);

        // Act
        var noseTrajectory = series.GetKeyPointTrajectory("nose").ToList();

        // Assert
        Assert.Equal(2, noseTrajectory.Count);
        Assert.Equal(100, noseTrajectory[0].point.X);
        Assert.Equal(110, noseTrajectory[1].point.X);
    }

    [Fact]
    public async Task GetKeyPoint_ByIdAndName_ReturnsCorrectValue()
    {
        // Arrange
        var stream = new MemoryStream();
        var storage = new KeyPointsSink(stream);

        using (var writer = storage.CreateWriter(frameId: 10))
        {
            writer.Append(keypointId: 0, x: 100, y: 200, confidence: 0.95f);
            writer.Append(keypointId: 1, x: 120, y: 190, confidence: 0.92f);
        }

        stream.Position = 0;
        using var frameSource = new StreamFrameSource(stream);
        var series = await storage.Read(TestDefinitionJson, frameSource);

        // Act & Assert - By ID
        var resultById = series.GetKeyPoint(frameId: 10, keypointId: 0);
        Assert.NotNull(resultById);
        Assert.Equal(new Point(100, 200), resultById!.Value.point);
        Assert.Equal(0.95f, resultById.Value.confidence, precision: 4);

        // Act & Assert - By Name
        var resultByName = series.GetKeyPoint(frameId: 10, keypointName: "nose");
        Assert.NotNull(resultByName);
        Assert.Equal(new Point(100, 200), resultByName!.Value.point);

        // Act & Assert - Non-existent
        var notFound = series.GetKeyPoint(frameId: 999, keypointId: 0);
        Assert.Null(notFound);
    }

    [Fact]
    public async Task ConfidenceEncoding_PreservesFloatPrecision()
    {
        // Arrange
        var stream = new MemoryStream();
        var storage = new KeyPointsSink(stream);

        var testConfidences = new[] { 0.0f, 0.5f, 0.9999f, 1.0f, 0.1234f };

        using (var writer = storage.CreateWriter(frameId: 1))
        {
            for (int i = 0; i < testConfidences.Length; i++)
            {
                writer.Append(keypointId: i, x: 100, y: 200, confidence: testConfidences[i]);
            }
        }

        stream.Position = 0;
        using var frameSource = new StreamFrameSource(stream);
        var series = await storage.Read(TestDefinitionJson, frameSource);

        // Assert - Check precision (should be within 0.0001 due to ushort encoding)
        var frame = series.GetFrame(1)!;
        for (int i = 0; i < testConfidences.Length; i++)
        {
            var actual = frame[i].confidence;
            Assert.Equal(testConfidences[i], actual, precision: 4);
        }
    }

    [Fact]
    public async Task VariableKeypointCount_HandledCorrectly()
    {
        // Arrange
        var stream = new MemoryStream();
        var storage = new KeyPointsSink(stream);

        // Frame 1 - 2 keypoints
        using (var writer1 = storage.CreateWriter(frameId: 0))
        {
            writer1.Append(keypointId: 0, x: 100, y: 200, confidence: 0.95f);
            writer1.Append(keypointId: 1, x: 120, y: 190, confidence: 0.92f);
        }

        // Frame 2 - 4 keypoints (2 new ones appeared)
        using (var writer2 = storage.CreateWriter(frameId: 1))
        {
            writer2.Append(keypointId: 0, x: 101, y: 201, confidence: 0.94f);
            writer2.Append(keypointId: 1, x: 121, y: 191, confidence: 0.93f);
            writer2.Append(keypointId: 3, x: 150, y: 300, confidence: 0.88f);
            writer2.Append(keypointId: 4, x: 50, y: 300, confidence: 0.85f);
        }

        // Frame 3 - 1 keypoint (most disappeared)
        using (var writer3 = storage.CreateWriter(frameId: 2))
        {
            writer3.Append(keypointId: 0, x: 102, y: 202, confidence: 0.96f);
        }

        stream.Position = 0;
        using var frameSource = new StreamFrameSource(stream);
        var series = await storage.Read(TestDefinitionJson, frameSource);

        // Assert
        Assert.Equal(2, series.GetFrame(0)!.Count);
        Assert.Equal(4, series.GetFrame(1)!.Count);
        Assert.Equal(1, series.GetFrame(2)!.Count);

        // Verify trajectory includes only frames where keypoint exists
        var id3Trajectory = series.GetKeyPointTrajectory(keypointId: 3).ToList();
        Assert.Single(id3Trajectory);
        Assert.Equal((ulong)1, id3Trajectory[0].frameId);
    }

    [Fact]
    public async Task LargeCoordinates_PreservesPrecision()
    {
        // Arrange
        var stream = new MemoryStream();
        var storage = new KeyPointsSink(stream);

        var testPoints = new[]
        {
            new Point(0, 0),
            new Point(-1000, -2000),
            new Point(int.MaxValue / 2, int.MaxValue / 2),
            new Point(int.MinValue / 2, int.MinValue / 2)
        };

        using (var writer = storage.CreateWriter(frameId: 1))
        {
            for (int i = 0; i < testPoints.Length; i++)
            {
                writer.Append(keypointId: i, testPoints[i], confidence: 1.0f);
            }
        }

        stream.Position = 0;
        using var frameSource = new StreamFrameSource(stream);
        var series = await storage.Read(TestDefinitionJson, frameSource);

        // Assert
        var frame = series.GetFrame(1)!;
        for (int i = 0; i < testPoints.Length; i++)
        {
            Assert.Equal(testPoints[i], frame[i].point);
        }
    }

    [Fact]
    public async Task AsyncWriter_RoundTrip_PreservesData()
    {
        // Arrange
        var stream = new MemoryStream();
        var storage = new KeyPointsSink(stream);

        var expectedKeypoints = new[]
        {
            (id: 0, point: new Point(100, 200), confidence: 0.95f),
            (id: 1, point: new Point(120, 190), confidence: 0.92f),
            (id: 2, point: new Point(80, 190), confidence: 0.88f)
        };

        // Act - Write using async methods
        await using (var writer = storage.CreateWriter(frameId: 1))
        {
            foreach (var (id, point, confidence) in expectedKeypoints)
            {
                await writer.AppendAsync(id, point, confidence);
            }
        }

        // Act - Read
        stream.Position = 0;
        using var frameSource = new StreamFrameSource(stream);
        var series = await storage.Read(TestDefinitionJson, frameSource);

        // Assert
        Assert.True(series.ContainsFrame(1));
        var frame = series.GetFrame(1)!;
        Assert.Equal(3, frame.Count);

        foreach (var (id, expectedPoint, expectedConfidence) in expectedKeypoints)
        {
            Assert.True(frame.ContainsKey(id));
            var result = frame[id];
            Assert.Equal(expectedPoint, result.point);
            Assert.Equal(expectedConfidence, result.confidence, precision: 4);
        }
    }
}
