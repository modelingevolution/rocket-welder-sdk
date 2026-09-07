using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using RocketWelder.SDK.Http.Programs;

namespace RocketWelder.SDK.Http.Tests.Programs;

public class AuthoringValueTypeTests
{
    [Fact]
    public void BlockId_Should_RoundTrip_Through_String()
    {
        var id = new BlockId("blk-42");

        ((string)id).Should().Be("blk-42");
        id.ToString().Should().Be("blk-42");
        BlockId.Parse("blk-42").Should().Be(id);
    }

    [Fact]
    public void BlockId_Should_Serialize_As_Bare_Json_String()
    {
        var json = JsonSerializer.Serialize(new BlockId("blk-42"));

        json.Should().Be("\"blk-42\"");
        JsonSerializer.Deserialize<BlockId>(json).Should().Be(new BlockId("blk-42"));
    }

    [Fact]
    public void ProgramEtag_Should_RoundTrip_And_Serialize_As_Bare_Json_String()
    {
        var etag = new ProgramEtag("sha256:abc");

        etag.ToString().Should().Be("sha256:abc");
        var json = JsonSerializer.Serialize(etag);
        json.Should().Be("\"sha256:abc\"");
        JsonSerializer.Deserialize<ProgramEtag>(json).Should().Be(etag);
    }

    [Theory]
    [InlineData("tail")]
    [InlineData("after:blk-1")]
    [InlineData("before:blk-9")]
    public void BlockAnchor_Should_RoundTrip_Its_Compact_Parsable_String(string compact)
    {
        // IParsable is the CLI/MCP string form, distinct from the JSON wire shape.
        var anchor = BlockAnchor.Parse(compact);
        anchor.ToString().Should().Be(compact);
    }

    [Fact]
    public void BlockAnchor_Should_Serialize_As_Object_With_Kind_And_Ref()
    {
        var json = JsonSerializer.Serialize(
            BlockAnchor.After(new BlockId("blk-1")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            });

        var node = JsonNode.Parse(json)!;
        node["kind"]!.GetValue<string>().Should().Be("After");
        node["ref"]!.GetValue<string>().Should().Be("blk-1");
    }

    [Fact]
    public void BlockAnchor_Tail_Should_Omit_Ref_On_The_Wire()
    {
        var json = JsonSerializer.Serialize(
            BlockAnchor.Tail,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            });

        var node = JsonNode.Parse(json)!;
        node["kind"]!.GetValue<string>().Should().Be("Tail");
        node.AsObject().ContainsKey("ref").Should().BeFalse();
    }

    [Fact]
    public void BlockAnchor_Should_Deserialize_From_Object_Shape()
    {
        var anchor = JsonSerializer.Deserialize<BlockAnchor>(
            """{ "kind": "Before", "ref": "blk-9" }""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        anchor.Kind.Should().Be(AnchorKind.Before);
        anchor.Ref.Should().Be(new BlockId("blk-9"));
    }

    [Fact]
    public void BlockAnchor_Factories_Should_Set_Kind_And_Ref()
    {
        BlockAnchor.Tail.Kind.Should().Be(AnchorKind.Tail);
        BlockAnchor.Tail.Ref.Should().BeNull();

        var after = BlockAnchor.After(new BlockId("blk-1"));
        after.Kind.Should().Be(AnchorKind.After);
        after.Ref.Should().Be(new BlockId("blk-1"));

        var before = BlockAnchor.Before(new BlockId("blk-9"));
        before.Kind.Should().Be(AnchorKind.Before);
        before.Ref.Should().Be(new BlockId("blk-9"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sideways:blk-1")]
    [InlineData("after:")]
    [InlineData("after")]
    public void BlockAnchor_TryParse_Should_Reject_Malformed_Input(string wire)
    {
        BlockAnchor.TryParse(wire, null, out _).Should().BeFalse();
    }
}
