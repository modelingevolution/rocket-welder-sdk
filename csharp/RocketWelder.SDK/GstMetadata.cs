using System.Text.Json.Serialization;

namespace RocketWelder.SDK
{
    /// <summary>
    /// Metadata structure that matches the JSON written by GStreamer plugins
    /// </summary>
    public record GstMetadata(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("caps")] GstCaps Caps,
        [property: JsonPropertyName("element_name")] string ElementName
    );
}