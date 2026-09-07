using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using ModelingEvolution.JsonParsableConverter;

namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Stable identifier for a block inside a program tree. Minted server-side when
/// the tree is loaded (rocket-welder2 <c>ProgramSource</c>) and preserved across
/// concurrent inserts — which a positional index is not. Every authoring endpoint
/// is keyed by this id, never by position.
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<BlockId>))]
public readonly record struct BlockId : IParsable<BlockId>
{
    private readonly string _value;

    public BlockId(string value) =>
        _value = value ?? throw new ArgumentNullException(nameof(value));

    public static implicit operator string(BlockId id) => id._value;
    public static explicit operator BlockId(string value) => new(value);

    public static BlockId Parse(string s, IFormatProvider? provider = null) =>
        TryParse(s, provider, out var result) ? result : throw new FormatException($"Invalid BlockId: '{s}'.");

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out BlockId result)
    {
        if (string.IsNullOrEmpty(s))
        {
            result = default;
            return false;
        }

        result = new BlockId(s);
        return true;
    }

    public override string ToString() => _value;
}
