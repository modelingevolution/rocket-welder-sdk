using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Position for an inserted or moved block, relative to a <see cref="BlockId"/>
/// (<see cref="AnchorKind.After"/>/<see cref="AnchorKind.Before"/>) or at the tail.
/// </summary>
/// <remarks>
/// On the wire it is a JSON object — <c>{ "kind": "After", "ref": "&lt;blockId&gt;" }</c>
/// (<c>ref</c> omitted for <see cref="AnchorKind.Tail"/>) — matching the rw2 REST
/// contract. <see cref="IParsable{T}"/> is retained for a compact string form
/// (<c>"tail"</c>/<c>"after:&lt;id&gt;"</c>/<c>"before:&lt;id&gt;"</c>) usable from CLI/MCP
/// arguments; that string is NOT the JSON wire shape.
/// </remarks>
public readonly record struct BlockAnchor : IParsable<BlockAnchor>
{
    [JsonConstructor]
    public BlockAnchor(AnchorKind kind, BlockId? @ref) => (Kind, Ref) = (kind, @ref);

    /// <summary>Placement mode relative to <see cref="Ref"/>.</summary>
    public AnchorKind Kind { get; }

    /// <summary>The block this anchor is relative to; null when <see cref="Kind"/> is <see cref="AnchorKind.Tail"/>.</summary>
    public BlockId? Ref { get; }

    /// <summary>Append at the end of the tree.</summary>
    public static BlockAnchor Tail => new(AnchorKind.Tail, null);

    /// <summary>Insert immediately after <paramref name="reference"/>.</summary>
    public static BlockAnchor After(BlockId reference) => new(AnchorKind.After, reference);

    /// <summary>Insert immediately before <paramref name="reference"/>.</summary>
    public static BlockAnchor Before(BlockId reference) => new(AnchorKind.Before, reference);

    /// <summary>Compact string form for CLI/MCP arguments — NOT the JSON wire shape.</summary>
    public override string ToString() => Kind switch
    {
        AnchorKind.Tail => "tail",
        AnchorKind.After => $"after:{Ref}",
        AnchorKind.Before => $"before:{Ref}",
        _ => throw new InvalidOperationException($"Unknown anchor kind {Kind}."),
    };

    public static BlockAnchor Parse(string s, IFormatProvider? provider = null) =>
        TryParse(s, provider, out var result) ? result : throw new FormatException($"Invalid BlockAnchor: '{s}'.");

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out BlockAnchor result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        s = s.Trim();
        if (string.Equals(s, "tail", StringComparison.OrdinalIgnoreCase))
        {
            result = Tail;
            return true;
        }

        var sep = s.IndexOf(':');
        if (sep <= 0 || sep == s.Length - 1)
            return false;

        var kind = s[..sep];
        if (!BlockId.TryParse(s[(sep + 1)..], provider, out var reference))
            return false;

        if (string.Equals(kind, "after", StringComparison.OrdinalIgnoreCase))
        {
            result = After(reference);
            return true;
        }

        if (string.Equals(kind, "before", StringComparison.OrdinalIgnoreCase))
        {
            result = Before(reference);
            return true;
        }

        return false;
    }
}
