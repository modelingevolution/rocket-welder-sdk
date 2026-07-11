namespace RocketWelder.SDK.Http.GstElements;

/// <summary>
/// <c>/api/gst/elements</c> — read access to the welder's GStreamer element
/// catalog (gst-inspect-like introspection served from the in-process catalog,
/// no shell-out). Lets a caller discover which elements exist and inspect one
/// element's properties before authoring or editing a pipeline.
/// </summary>
public interface IGstElementsApi
{
    /// <summary>
    /// <c>GET /api/gst/elements</c> — every element in the catalog as a
    /// name/description summary. Returns an empty list if the catalog is empty.
    /// </summary>
    Task<IReadOnlyList<GstElementSummary>> ListElementsAsync(CancellationToken ct = default);

    /// <summary>
    /// <c>GET /api/gst/elements/{name}</c> — full help (every property with its
    /// type, default, description, read/write flags, category and numeric range)
    /// for one element. Null when no element with that catalog name exists (the
    /// server answers 404).
    /// </summary>
    Task<GstElementHelp?> GetElementHelpAsync(string name, CancellationToken ct = default);
}
