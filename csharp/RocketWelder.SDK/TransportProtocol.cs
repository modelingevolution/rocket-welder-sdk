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
    /// <summary>Unix domain socket (direct, no messaging library).</summary>
    Socket,
    /// <summary>NNG Push over IPC.</summary>
    NngPushIpc,
    /// <summary>NNG Push over TCP.</summary>
    NngPushTcp,
    /// <summary>NNG Pull over IPC.</summary>
    NngPullIpc,
    /// <summary>NNG Pull over TCP.</summary>
    NngPullTcp,
    /// <summary>NNG Pub over IPC.</summary>
    NngPubIpc,
    /// <summary>NNG Pub over TCP.</summary>
    NngPubTcp,
    /// <summary>NNG Sub over IPC.</summary>
    NngSubIpc,
    /// <summary>NNG Sub over TCP.</summary>
    NngSubTcp,
}

/// <summary>
/// Unified transport protocol specification as a value type.
/// Supports: file://, socket://, nng+push+ipc://, nng+push+tcp://, etc.
/// </summary>
/// <remarks>
/// Examples:
/// <code>
/// file:///home/user/output.bin   - absolute file path
/// file://relative/path.bin       - relative file path
/// socket:///tmp/my.sock          - Unix domain socket
/// nng+push+ipc://tmp/keypoints   - NNG Push over IPC
/// nng+push+tcp://host:5555       - NNG Push over TCP
/// </code>
/// </remarks>
public readonly record struct TransportProtocol : IParsable<TransportProtocol>
{
    /// <summary>The transport kind.</summary>
    public TransportKind Kind { get; }

    /// <summary>The schema string (e.g., "file", "socket", "nng+push+ipc").</summary>
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

    /// <summary>NNG Push over IPC.</summary>
    public static readonly TransportProtocol NngPushIpc = new(TransportKind.NngPushIpc, "nng+push+ipc");

    /// <summary>NNG Push over TCP.</summary>
    public static readonly TransportProtocol NngPushTcp = new(TransportKind.NngPushTcp, "nng+push+tcp");

    /// <summary>NNG Pull over IPC.</summary>
    public static readonly TransportProtocol NngPullIpc = new(TransportKind.NngPullIpc, "nng+pull+ipc");

    /// <summary>NNG Pull over TCP.</summary>
    public static readonly TransportProtocol NngPullTcp = new(TransportKind.NngPullTcp, "nng+pull+tcp");

    /// <summary>NNG Pub over IPC.</summary>
    public static readonly TransportProtocol NngPubIpc = new(TransportKind.NngPubIpc, "nng+pub+ipc");

    /// <summary>NNG Pub over TCP.</summary>
    public static readonly TransportProtocol NngPubTcp = new(TransportKind.NngPubTcp, "nng+pub+tcp");

    /// <summary>NNG Sub over IPC.</summary>
    public static readonly TransportProtocol NngSubIpc = new(TransportKind.NngSubIpc, "nng+sub+ipc");

    /// <summary>NNG Sub over TCP.</summary>
    public static readonly TransportProtocol NngSubTcp = new(TransportKind.NngSubTcp, "nng+sub+tcp");

    #endregion

    #region Classification properties

    /// <summary>True if this is a file transport.</summary>
    public bool IsFile => Kind == TransportKind.File;

    /// <summary>True if this is a Unix socket transport.</summary>
    public bool IsSocket => Kind == TransportKind.Socket;

    /// <summary>True if this is any NNG-based transport.</summary>
    public bool IsNng => Kind is TransportKind.NngPushIpc or TransportKind.NngPushTcp
                               or TransportKind.NngPullIpc or TransportKind.NngPullTcp
                               or TransportKind.NngPubIpc or TransportKind.NngPubTcp
                               or TransportKind.NngSubIpc or TransportKind.NngSubTcp;

    /// <summary>True if this is a Push pattern.</summary>
    public bool IsPush => Kind is TransportKind.NngPushIpc or TransportKind.NngPushTcp;

    /// <summary>True if this is a Pull pattern.</summary>
    public bool IsPull => Kind is TransportKind.NngPullIpc or TransportKind.NngPullTcp;

    /// <summary>True if this is a Pub pattern.</summary>
    public bool IsPub => Kind is TransportKind.NngPubIpc or TransportKind.NngPubTcp;

    /// <summary>True if this is a Sub pattern.</summary>
    public bool IsSub => Kind is TransportKind.NngSubIpc or TransportKind.NngSubTcp;

    /// <summary>True if this uses IPC layer.</summary>
    public bool IsIpc => Kind is TransportKind.NngPushIpc or TransportKind.NngPullIpc
                               or TransportKind.NngPubIpc or TransportKind.NngSubIpc;

    /// <summary>True if this uses TCP layer.</summary>
    public bool IsTcp => Kind is TransportKind.NngPushTcp or TransportKind.NngPullTcp
                               or TransportKind.NngPubTcp or TransportKind.NngSubTcp;

    #endregion

    /// <summary>
    /// Creates the NNG address from a path/host.
    /// For IPC: ipc:///path
    /// For TCP: tcp://host:port
    /// </summary>
    public string CreateNngAddress(string pathOrHost)
    {
        if (!IsNng)
            throw new InvalidOperationException($"Cannot create NNG address for {Kind} transport");

        if (IsIpc)
        {
            // IPC paths need leading "/" for absolute paths
            if (!pathOrHost.StartsWith("/"))
                return "ipc:///" + pathOrHost;
            return "ipc://" + pathOrHost;
        }

        // TCP
        return "tcp://" + pathOrHost;
    }

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
            "nng+push+ipc" => NngPushIpc,
            "nng+push+tcp" => NngPushTcp,
            "nng+pull+ipc" => NngPullIpc,
            "nng+pull+tcp" => NngPullTcp,
            "nng+pub+ipc" => NngPubIpc,
            "nng+pub+tcp" => NngPubTcp,
            "nng+sub+ipc" => NngSubIpc,
            "nng+sub+tcp" => NngSubTcp,
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
