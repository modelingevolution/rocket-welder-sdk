using System.Text.Json;
using FluentAssertions;
using RocketWelder.SDK.Shared;

namespace RocketWelder.SDK.Shared.Tests;

public class SessionStreamIdTests
{
    [Fact]
    public void New_ShouldCreateUniqueIds()
    {
        // Act
        var id1 = SessionStreamId.New();
        var id2 = SessionStreamId.New();

        // Assert
        id1.Should().NotBe(id2);
        id1.Should().NotBe(SessionStreamId.Empty);
    }

    [Fact]
    public void From_ShouldWrapGuid()
    {
        // Arrange
        var guid = Guid.NewGuid();

        // Act
        var id = SessionStreamId.From(guid);

        // Assert
        ((Guid)id).Should().Be(guid);
    }

    [Fact]
    public void ToString_ShouldHaveCorrectFormat()
    {
        // Arrange
        var guid = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        var id = SessionStreamId.From(guid);

        // Act
        string str = id.ToString();

        // Assert
        str.Should().StartWith("ps-");
        str.Should().Be("ps-a1b2c3d4-e5f6-7890-abcd-ef1234567890");
    }

    [Fact]
    public void Parse_ShouldRoundtrip()
    {
        // Arrange
        var original = SessionStreamId.New();
        string str = original.ToString();

        // Act
        var parsed = SessionStreamId.Parse(str);

        // Assert
        parsed.Should().Be(original);
    }

    [Fact]
    public void TryParse_ValidString_ShouldSucceed()
    {
        // Arrange
        string str = "ps-a1b2c3d4-e5f6-7890-abcd-ef1234567890";

        // Act
        bool success = SessionStreamId.TryParse(str, null, out var result);

        // Assert
        success.Should().BeTrue();
        result.ToString().Should().Be(str);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("px-a1b2c3d4-e5f6-7890-abcd-ef1234567890")]
    [InlineData("ps-invalid-guid")]
    public void TryParse_InvalidString_ShouldFail(string? str)
    {
        // Act
        bool success = SessionStreamId.TryParse(str, null, out _);

        // Assert
        success.Should().BeFalse();
    }

    [Fact]
    public void Parse_InvalidPrefix_ShouldThrow()
    {
        // Arrange
        string str = "invalid-a1b2c3d4-e5f6-7890-abcd-ef1234567890";

        // Act & Assert
        var act = () => SessionStreamId.Parse(str);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void JsonSerializationRoundtrip_ShouldWork()
    {
        // Arrange
        var original = SessionStreamId.New();

        // Act
        string json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<SessionStreamId>(json);

        // Assert
        deserialized.Should().Be(original);
    }

    [Fact]
    public void ImplicitConversion_ToGuid_ShouldWork()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var id = SessionStreamId.From(guid);

        // Act
        Guid result = id;

        // Assert
        result.Should().Be(guid);
    }

    [Fact]
    public void ImplicitConversion_ToString_ShouldWork()
    {
        // Arrange
        var id = SessionStreamId.New();

        // Act
        string result = id;

        // Assert
        result.Should().StartWith("ps-");
    }

    [Fact]
    public void Empty_ShouldHaveEmptyGuid()
    {
        // Assert
        ((Guid)SessionStreamId.Empty).Should().Be(Guid.Empty);
    }
}
