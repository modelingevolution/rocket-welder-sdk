using System.Text.Json;
using FluentAssertions;
using RocketWelder.SDK.Types;

namespace RocketWelder.SDK.Types.Tests;

public class FrameIdTests
{
    private static readonly FrameId TestFrameId =
        new FrameId(new VideoRecordingIdentifier(HostName.Localhost, 1, DateTimeOffset.UnixEpoch), 69);

    [Fact]
    public void Parse_ShouldMaintainEquality()
    {
        // Act
        var parsed = FrameId.Parse(TestFrameId.ToString());

        // Assert
        parsed.Should().Be(TestFrameId);
    }

    [Fact]
    public void JsonSerializeAndDeserialize_ShouldMaintainEquality()
    {
        // Arrange
        var options = new JsonSerializerOptions { WriteIndented = true };

        // Act
        string jsonString = JsonSerializer.Serialize(TestFrameId, options);
        var deserialized = JsonSerializer.Deserialize<FrameId>(jsonString);

        // Assert
        deserialized.Should().Be(TestFrameId);
    }

    [Fact]
    public void ToStringFileName_ShouldParseBack()
    {
        // Arrange
        var recording = new VideoRecordingIdentifier(HostName.Parse("host"), 1, DateTimeOffset.UtcNow);
        var frameId = new FrameId(recording, 123);

        // Act
        string fileName = frameId.ToStringFileName();
        bool success = FrameId.TryParseFileName(fileName, out var parsed);

        // Assert
        success.Should().BeTrue();
        parsed.Recording.HostName.Should().Be(recording.HostName);
        parsed.FrameNumber.Should().Be(123ul);
    }

    [Fact]
    public void ToGuid_ShouldBeDeterministic()
    {
        // Act
        Guid guid1 = TestFrameId.ToGuid();
        Guid guid2 = TestFrameId.ToGuid();

        // Assert
        guid1.Should().Be(guid2);
        guid1.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void CompareTo_ShouldOrderCorrectly()
    {
        // Arrange
        var recording = new VideoRecordingIdentifier(HostName.Parse("host"), DateTimeOffset.UtcNow);
        var frame1 = new FrameId(recording, 1);
        var frame2 = new FrameId(recording, 2);

        // Act & Assert
        frame1.CompareTo(frame2).Should().BeLessThan(0);
        frame2.CompareTo(frame1).Should().BeGreaterThan(0);
        frame1.CompareTo(frame1).Should().Be(0);
    }
}
