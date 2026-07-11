namespace RocketWelder.SDK.Http.GstElements;

/// <summary>
/// Wire shape for the full help of a single GStreamer element.
/// Backs <c>GET /api/gst/elements/{name}</c>.
/// </summary>
/// <param name="Name">Catalog name of the element.</param>
/// <param name="Description">Short human-readable description of the element.</param>
/// <param name="Properties">Every property the element exposes — see <see cref="GstPropertyHelp"/>.</param>
public sealed record GstElementHelp(
    string Name,
    string Description,
    GstPropertyHelp[] Properties);
