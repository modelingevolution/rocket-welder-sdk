using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using ModelingEvolution.JsonParsableConverter;

namespace RocketWelder.SDK;

/// <summary>
/// Strongly-typed identifier for streaming sessions.
/// Format: ps-{guid} (e.g., ps-a1b2c3d4-e5f6-7890-abcd-ef1234567890)
///
/// Prefix "ps" = PipelineSession, allows identification when parsing from string.
/// Stores only Guid (16 bytes) - prefix is constant, not stored.
/// Comparison is just Guid comparison - O(1), very fast.
/// Value is intentionally NOT exposed - use ToString() for string representation.
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<SessionStreamId>))]
public readonly record struct SessionStreamId : IParsable<SessionStreamId>, ISpanParsable<SessionStreamId>
{
    private const string Prefix = "ps-";
    private const int PrefixLength = 3; // "ps-"

    private readonly Guid _value;

    private SessionStreamId(Guid value) => _value = value;

    /// <summary>
    /// Create new SessionStreamId with random Guid.
    /// </summary>
    public static SessionStreamId New() => new(Guid.NewGuid());

    /// <summary>
    /// Create SessionStreamId from existing Guid.
    /// </summary>
    public static SessionStreamId From(Guid guid) => new(guid);

    public static SessionStreamId Empty => new(Guid.Empty);

    /// <summary>
    /// Implicit conversion to Guid - for URL generation and internal operations.
    /// </summary>
    public static implicit operator Guid(SessionStreamId id) => id._value;

    /// <summary>
    /// String format: ps-{guid}
    /// </summary>
    public override string ToString() => $"{Prefix}{_value}";

    // IParsable<T>
    public static SessionStreamId Parse(string s, IFormatProvider? provider = null)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (!s.StartsWith(Prefix, StringComparison.Ordinal))
            throw new FormatException($"SessionStreamId must start with '{Prefix}'");

        return new(Guid.Parse(s.AsSpan(PrefixLength)));
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out SessionStreamId result)
    {
        result = default;
        if (s is null || s.Length < PrefixLength + 32) // prefix + min guid length
            return false;
        if (!s.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        if (!Guid.TryParse(s.AsSpan(PrefixLength), out var guid))
            return false;

        result = new(guid);
        return true;
    }

    // ISpanParsable<T>
    public static SessionStreamId Parse(ReadOnlySpan<char> s, IFormatProvider? provider = null)
    {
        if (s.Length < PrefixLength)
            throw new FormatException($"SessionStreamId must start with '{Prefix}'");
        if (!s[..PrefixLength].SequenceEqual(Prefix.AsSpan()))
            throw new FormatException($"SessionStreamId must start with '{Prefix}'");

        return new(Guid.Parse(s[PrefixLength..]));
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out SessionStreamId result)
    {
        result = default;
        if (s.Length < PrefixLength + 32)
            return false;
        if (!s[..PrefixLength].SequenceEqual(Prefix.AsSpan()))
            return false;
        if (!Guid.TryParse(s[PrefixLength..], out var guid))
            return false;

        result = new(guid);
        return true;
    }

    // Implicit conversion to string for convenience
    public static implicit operator string(SessionStreamId id) => id.ToString();
}

/// <summary>
/// Extension methods for generating NNG IPC URLs from SessionStreamId.
/// </summary>
public static class SessionStreamIdExtensions
{
    /// <summary>
    /// Get NNG IPC URL for segmentation stream.
    /// </summary>
    public static string ToSegmentationUrl(this SessionStreamId id) =>
        $"ipc:///tmp/rw-{(Guid)id}-seg.sock";

    /// <summary>
    /// Get NNG IPC URL for keypoints stream.
    /// </summary>
    public static string ToKeypointsUrl(this SessionStreamId id) =>
        $"ipc:///tmp/rw-{(Guid)id}-kp.sock";

    /// <summary>
    /// Get NNG IPC URL for actions stream.
    /// </summary>
    public static string ToActionsUrl(this SessionStreamId id) =>
        $"ipc:///tmp/rw-{(Guid)id}-actions.sock";
}
