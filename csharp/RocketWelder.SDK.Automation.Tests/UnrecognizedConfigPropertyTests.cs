using System.Text.Json;
using FluentAssertions;

namespace RocketWelder.SDK.Automation.Tests;

/// <summary>
/// A config property whose name this process does not know must deserialize, not throw. The throw used to land
/// inside the devices read model's subscription, which then retried the same event forever and never caught up.
/// </summary>
public class UnrecognizedConfigPropertyTests
{
    // The shape PeripheralDeviceConfigured carries: a known key next to one owned by a plugin that is not loaded.
    private const string Mixed =
        """[{"Name":"Port","Value":"8080"},{"Name":"some.plugin.NotLoadedHere","Value":"42"}]""";

    // What a host does at startup: the SDK's own property names are known, the plugin's are not.
    public UnrecognizedConfigPropertyTests() => ConfigPropertyJsonConverter.ScanAssembly<PortProperty>();

    [Fact]
    public void An_unknown_name_is_kept_verbatim_instead_of_throwing()
    {
        var set = JsonSerializer.Deserialize<ConfigSet>(Mixed)!;

        var unknown = set.Single(x => x.Name == "some.plugin.NotLoadedHere").Value;
        var kept = unknown.Should().BeOfType<UnrecognizedProperty>().Subject;
        kept.Name.Should().Be("some.plugin.NotLoadedHere");
        kept.Value.Should().Be("42");
    }

    [Fact]
    public void The_known_properties_beside_it_still_deserialize_as_themselves()
    {
        var set = JsonSerializer.Deserialize<ConfigSet>(Mixed)!;

        set.Single(x => x.Name == "Port").Value.Should().BeOfType<PortProperty>()
           .Which.Value.Should().Be(8080);
    }

    [Fact]
    public void An_unknown_property_round_trips_so_the_next_save_does_not_drop_it()
    {
        var set = JsonSerializer.Deserialize<ConfigSet>(Mixed)!;

        var again = JsonSerializer.Deserialize<ConfigSet>(JsonSerializer.Serialize(set))!;

        again.Single(x => x.Name == "some.plugin.NotLoadedHere").Value
             .Should().Be(new UnrecognizedProperty("some.plugin.NotLoadedHere", "42"));
    }

    [Fact]
    public void An_item_with_no_name_is_still_malformed()
    {
        var act = () => JsonSerializer.Deserialize<ConfigSet>("""[{"Value":"42"}]""");

        act.Should().Throw<JsonException>();
    }
}
