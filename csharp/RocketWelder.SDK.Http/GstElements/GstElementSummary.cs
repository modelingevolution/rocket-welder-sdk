namespace RocketWelder.SDK.Http.GstElements;

/// <summary>
/// Wire shape for one entry of the GStreamer element catalog.
/// Backs <c>GET /api/gst/elements</c>.
/// </summary>
/// <param name="Name">Catalog name of the element (e.g. <c>queue</c>) — the key used by
/// <c>GET /api/gst/elements/{name}</c>.</param>
/// <param name="Description">Short human-readable description of the element.</param>
public sealed record GstElementSummary(
    string Name,
    string Description);
