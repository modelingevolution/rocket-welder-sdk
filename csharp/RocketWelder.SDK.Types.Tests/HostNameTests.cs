using System.Text.Json;
using FluentAssertions;
using RocketWelder.SDK.Types;

namespace RocketWelder.SDK.Types.Tests;

public class HostNameTests
{
    [Fact]
    public void Parse_ShouldCreateHostName()
    {
        // Act
        var hostName = HostName.Parse("myhost");

        // Assert
        hostName.ToString().Should().Be("myhost");
    }

    [Fact]
    public void From_ShouldCreateHostName()
    {
        // Act
        var hostName = HostName.From("myhost");

        // Assert
        hostName.ToString().Should().Be("myhost");
    }

    [Fact]
    public void Equals_ShouldBeCaseInsensitive()
    {
        // Arrange
        var host1 = HostName.Parse("MyHost");
        var host2 = HostName.Parse("myhost");
        var host3 = HostName.Parse("MYHOST");

        // Assert
        host1.Should().Be(host2);
        host2.Should().Be(host3);
        host1.Should().Be(host3);
    }

    [Fact]
    public void CompareTo_ShouldBeCaseInsensitive()
    {
        // Arrange
        var host1 = HostName.Parse("Alpha");
        var host2 = HostName.Parse("beta");

        // Assert
        host1.CompareTo(host2).Should().BeLessThan(0);
    }

    [Fact]
    public void Empty_ShouldBeEmptyString()
    {
        // Assert
        HostName.Empty.ToString().Should().BeEmpty();
    }

    [Fact]
    public void Localhost_ShouldBeLocalhost()
    {
        // Assert
        HostName.Localhost.ToString().Should().Be("localhost");
    }

    [Fact]
    public void ImplicitConversion_ToString_ShouldWork()
    {
        // Arrange
        var hostName = HostName.Parse("test");

        // Act
        string result = hostName;

        // Assert
        result.Should().Be("test");
    }

    [Fact]
    public void ExplicitConversion_FromString_ShouldWork()
    {
        // Act
        var hostName = (HostName)"test";

        // Assert
        hostName.ToString().Should().Be("test");
    }

    [Fact]
    public void JsonSerializationRoundtrip_ShouldWork()
    {
        // Arrange
        var original = HostName.Parse("myhost");

        // Act
        string json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<HostName>(json);

        // Assert
        deserialized.Should().Be(original);
    }

    [Fact]
    public void GetHashCode_ShouldBeCaseInsensitive()
    {
        // Arrange
        var host1 = HostName.Parse("MyHost");
        var host2 = HostName.Parse("myhost");

        // Assert
        host1.GetHashCode().Should().Be(host2.GetHashCode());
    }

    [Fact]
    public void Operators_Equality_ShouldWork()
    {
        // Arrange
        var host1 = HostName.Parse("test");
        var host2 = HostName.Parse("test");
        var host3 = HostName.Parse("other");

        // Assert
        (host1 == host2).Should().BeTrue();
        (host1 != host3).Should().BeTrue();
    }
}
