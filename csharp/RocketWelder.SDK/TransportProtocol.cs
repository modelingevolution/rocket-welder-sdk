using System;
using System.Diagnostics.CodeAnalysis;

namespace RocketWelder.SDK;

/// <summary>
/// Transport kind enumeration.
/// </summary>
public enum TransportKind
{
    /// <summary>File output.</summary>
    File,
    /// <summary>Unix domain socket.</summary>
    Socket,
}

/// <summary>
/// Unified transport protocol specification as a value type.
/// Supports: file://, socket://
/// </summary>
/// <remarks>
/// Examples:
/// <code>
/// file:///home/user/output.bin   - absolute file path
/// file://relative/path.bin       - relative file path
/// socket:///tmp/my.sock          - Unix domain socket
/// </code>
/// </remarks>
public readonly record struct TransportProtocol : IParsable<TransportProtocol>
{
    /// <summary>The transport kind.</summary>
    public TransportKind Kind { get; }

    /// <summary>The schema string (e.g., "file", "socket").</summary>
    public string Schema { get; }

    private TransportProtocol(TransportKind kind, string schema)
    {
        Kind = kind;
        Schema = schema;
    }

    #region Predefined protocols

    /// <summary>File transport.</summary>
    public static readonly TransportProtocol File = new(TransportKind.File, "file");

    /// <summary>Unix domain socket transport.</summary>
    public static readonly TransportProtocol Socket = new(TransportKind.Socket, "socket");

    #endregion

    #region Classification properties

    /// <summary>True if this is a file transport.</summary>
    public bool IsFile => Kind == TransportKind.File;

    /// <summary>True if this is a Unix socket transport.</summary>
    public bool IsSocket => Kind == TransportKind.Socket;

    #endregion

    public override string ToString() => Schema;

    #region IParsable implementation

    public static TransportProtocol Parse(string s, IFormatProvider? provider)
    {
        if (!TryParse(s, provider, out var result))
            throw new FormatException($"Invalid transport protocol: {s}");
        return result;
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out TransportProtocol result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        // Normalize to lowercase for comparison
        var schema = s.ToLowerInvariant();

        result = schema switch
        {
            "file" => File,
            "socket" => Socket,
            _ => default
        };

        return result.Schema != null;
    }

    /// <summary>
    /// Tries to parse a protocol string (convenience overload without provider).
    /// </summary>
    public static bool TryParse(string? s, out TransportProtocol result)
        => TryParse(s, null, out result);

    #endregion
}
