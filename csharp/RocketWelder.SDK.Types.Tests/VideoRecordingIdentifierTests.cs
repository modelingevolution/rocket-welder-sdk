using System.Text.Json;
using FluentAssertions;
using RocketWelder.SDK.Types;

namespace RocketWelder.SDK.Types.Tests;

public class VideoRecordingIdentifierTests
{
    [Fact]
    public void Roundtrip_WithCamera_ShouldPreserveValues()
    {
        // Arrange
        var hostName = HostName.Parse("localhost");
        var cameraNumber = 1;
        var createdTime = new DateTimeOffset(2025, 1, 21, 12, 28, 54, 88, TimeSpan.FromHours(1));
        var identifier = new VideoRecordingIdentifier(hostName, cameraNumber, createdTime);

        // Act
        string fileName = identifier.ToStringFileName();
        bool parseSuccess = VideoRecordingIdentifier.TryParseFileName(fileName, out var parsedIdentifier);

        // Assert
        parseSuccess.Should().BeTrue();
        parsedIdentifier.HostName.Should().Be(identifier.HostName);
        parsedIdentifier.CameraNumber.Should().Be(identifier.CameraNumber);
        parsedIdentifier.CreatedTime.Should().Be(identifier.CreatedTime);
    }

    [Fact]
    public void Roundtrip_WithoutCamera_ShouldPreserveValues()
    {
        // Arrange
        var hostName = HostName.Parse("localhost");
        var createdTime = new DateTimeOffset(2025, 1, 21, 12, 28, 54, 555, TimeSpan.FromHours(1));
        var identifier = new VideoRecordingIdentifier(hostName, createdTime);

        // Act
        string fileName = identifier.ToStringFileName();
        bool parseSuccess = VideoRecordingIdentifier.TryParseFileName(fileName, out var parsedIdentifier);

        // Assert
        parseSuccess.Should().BeTrue();
        parsedIdentifier.HostName.Should().Be(identifier.HostName);
        parsedIdentifier.CameraNumber.Should().Be(identifier.CameraNumber);
        parsedIdentifier.CreatedTime.Should().Be(identifier.CreatedTime);
    }

    [Theory]
    [InlineData("localhost.1.20250121T122854.607435+0100")]  // With camera, with offset
    [InlineData("localhost.20250121T122854.607435+0100")]    // No camera, with offset
    [InlineData("localhost.1.20250121T122854.607435Z")]      // With camera, UTC
    [InlineData("localhost.20250121T122854.607435Z")]        // No camera, UTC
    [InlineData("test-host.2.20250121T122854.607435-0500")] // With camera, negative offset
    [InlineData("test-host.20250121T122854.607435Z")]       // No camera, UTC
    public void TryParseFileName_ValidFormats_ShouldSucceed(string fileName)
    {
        // Act
        bool result = VideoRecordingIdentifier.TryParseFileName(fileName, out _);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("localhost")]
    [InlineData("localhost.1")]
    [InlineData("localhost.1.invalid")]
    [InlineData("localhost.1.20250121")]
    [InlineData("localhost.1.20250121T122854")]
    [InlineData("localhost.1.20250121T122854f")]
    [InlineData("localhost.1.20250121T122854fz")]
    [InlineData("localhost.1.20250121T122854f123z")]
    [InlineData("localhost.invalid.20250121T122854f6074356z0100")]
    [InlineData("localhost.1.2.20250121T122854f6074356z0100")]
    public void TryParseFileName_InvalidFormats_ShouldFail(string? fileName)
    {
        // Act
        bool result = VideoRecordingIdentifier.TryParseFileName(fileName!, out _);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ToStringFileName_WithVariousFractions_ShouldHandleCorrectly()
    {
        // Arrange
        var hostName = HostName.Parse("localhost");
        var times = new[]
        {
            new DateTimeOffset(2025, 1, 21, 12, 28, 54, 100, TimeSpan.FromHours(1)),
            new DateTimeOffset(2025, 1, 21, 12, 28, 54, 120, TimeSpan.FromHours(1)),
            new DateTimeOffset(2025, 1, 21, 12, 28, 54, 0, TimeSpan.FromHours(1))
        };

        foreach (var time in times)
        {
            // Act
            var identifier = new VideoRecordingIdentifier(hostName, time);
            string fileName = identifier.ToStringFileName();
            bool parseSuccess = VideoRecordingIdentifier.TryParseFileName(fileName, out var parsedIdentifier);

            // Assert
            parseSuccess.Should().BeTrue();
            parsedIdentifier.CreatedTime.Should().Be(identifier.CreatedTime);
        }
    }

    [Fact]
    public void ToStringFileName_WithDifferentTimeZones_ShouldHandleCorrectly()
    {
        // Arrange
        var hostName = HostName.Parse("localhost");
        var offsets = new[]
        {
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(-5),
            TimeSpan.FromHours(5.5),
            TimeSpan.Zero
        };

        foreach (var offset in offsets)
        {
            // Act
            var time = new DateTimeOffset(2025, 1, 21, 12, 28, 54, 607, offset);
            var identifier = new VideoRecordingIdentifier(hostName, time);
            string fileName = identifier.ToStringFileName();
            bool parseSuccess = VideoRecordingIdentifier.TryParseFileName(fileName, out var parsedIdentifier);

            // Assert
            parseSuccess.Should().BeTrue();
            parsedIdentifier.CreatedTime.Should().Be(identifier.CreatedTime);
        }
    }

    [Fact]
    public void JsonSerializationRoundTrip_CameraIdentifier_ShouldMatch()
    {
        // Arrange
        var original = new VideoRecordingIdentifier
        {
            HostName = HostName.Parse("TestHost"),
            CameraNumber = 5,
            CreatedTime = new DateTimeOffset(2024, 1, 16, 12, 0, 0, 99, 22, TimeSpan.Zero)
        };

        // Act
        string json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<VideoRecordingIdentifier>(json);

        // Assert
        deserialized.HostName.Should().Be(original.HostName);
        deserialized.CameraNumber.Should().Be(original.CameraNumber);
        deserialized.CreatedTime.Should().Be(original.CreatedTime);
    }

    [Fact]
    public void JsonSerializationRoundTrip_FileIdentifier_ShouldMatch()
    {
        // Arrange
        var original = new VideoRecordingIdentifier
        {
            HostName = HostName.Parse("TestHost"),
            CameraNumber = null,
            CreatedTime = new DateTimeOffset(2024, 1, 16, 12, 0, 0, 99, 11, TimeSpan.Zero)
        };

        // Act
        string json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<VideoRecordingIdentifier>(json);

        // Assert
        deserialized.HostName.Should().Be(original.HostName);
        deserialized.CameraNumber.Should().BeNull();
        deserialized.CreatedTime.Should().Be(original.CreatedTime);
    }

    [Fact]
    public void JsonSerialization_InvalidJson_ShouldThrowException()
    {
        // Arrange
        string invalidJson = "{\"InvalidProperty\":\"value\"}";

        // Act & Assert
        var act = () => JsonSerializer.Deserialize<VideoRecordingIdentifier>(invalidJson);
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void ToGuid_ShouldReturnDeterministicGuid()
    {
        // Arrange
        var identifier = new VideoRecordingIdentifier(HostName.Parse("test"), 1, DateTimeOffset.UnixEpoch);

        // Act
        Guid guid1 = identifier.ToGuid();
        Guid guid2 = identifier.ToGuid();

        // Assert
        guid1.Should().Be(guid2);
        guid1.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void ImplicitConversion_ToGuid_ShouldWork()
    {
        // Arrange
        var identifier = new VideoRecordingIdentifier(HostName.Parse("test"), DateTimeOffset.UtcNow);

        // Act
        Guid guid = identifier;

        // Assert
        guid.Should().NotBe(Guid.Empty);
    }
}
