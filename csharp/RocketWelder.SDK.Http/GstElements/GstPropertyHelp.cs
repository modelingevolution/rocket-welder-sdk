namespace RocketWelder.SDK.Http.GstElements;

/// <summary>
/// Wire shape for one property of a GStreamer element (gst-inspect-like).
/// Part of <see cref="GstElementHelp"/> (<c>GET /api/gst/elements/{name}</c>).
/// Mirrors the server's per-property projection exactly — one wire contract.
/// </summary>
/// <param name="Name">Property name (e.g. <c>max-size-buffers</c>).</param>
/// <param name="Description">Property description, or null when the element declares none.</param>
/// <param name="Type">CLR value-type name of the property (e.g. <c>UInt32</c>, <c>Boolean</c>,
/// or an enum type name). Null when the type cannot be resolved.</param>
/// <param name="DefaultValue">Default value rendered as a string, or null when there is no default.</param>
/// <param name="Readable">True when the property can be read (derived from the element's GStreamer flags).</param>
/// <param name="Writable">True when the property can be written (derived from the element's GStreamer flags).</param>
/// <param name="Category">Property category (e.g. <c>Primitive</c>, <c>Caps</c>, <c>File</c>).</param>
/// <param name="Min">Inclusive lower bound for numeric properties, as a string; null for non-numeric properties.</param>
/// <param name="Max">Inclusive upper bound for numeric properties, as a string; null for non-numeric properties.</param>
public sealed record GstPropertyHelp(
    string Name,
    string? Description,
    string? Type,
    string? DefaultValue,
    bool Readable,
    bool Writable,
    string Category,
    string? Min,
    string? Max);
