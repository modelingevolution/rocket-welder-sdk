using System;

namespace RocketWelder.SDK.HighLevel;

/// <summary>
/// Messaging library (nng, zeromq, etc.).
/// </summary>
public readonly record struct MessagingLibrary
{
    public string Name { get; }

    private MessagingLibrary(string name) => Name = name;

    /// <summary>NNG (nanomsg next generation) library.</summary>
    public static readonly MessagingLibrary Nng = new("nng");

    public static TransportBuilder operator +(MessagingLibrary lib, MessagingPattern pattern)
        => new(lib, pattern);

    public override string ToString() => Name;
}

/// <summary>
/// Messaging pattern (push/pull, pub/sub, etc.).
/// </summary>
public readonly record struct MessagingPattern
{
    public string Name { get; }

    private MessagingPattern(string name) => Name = name;

    /// <summary>Push pattern (sender side of push/pull).</summary>
    public static readonly MessagingPattern Push = new("push");

    /// <summary>Pull pattern (receiver side of push/pull).</summary>
    public static readonly MessagingPattern Pull = new("pull");

    /// <summary>Pub pattern (sender side of pub/sub).</summary>
    public static readonly MessagingPattern Pub = new("pub");

    /// <summary>Sub pattern (receiver side of pub/sub).</summary>
    public static readonly MessagingPattern Sub = new("sub");

    public override string ToString() => Name;
}

/// <summary>
/// Transport layer (ipc, tcp, etc.).
/// </summary>
public readonly record struct TransportLayer
{
    public string Name { get; }
    public string UriPrefix { get; }

    private TransportLayer(string name, string uriPrefix)
    {
        Name = name;
        UriPrefix = uriPrefix;
    }

    /// <summary>IPC (inter-process communication via Unix domain sockets).</summary>
    public static readonly TransportLayer Ipc = new("ipc", "ipc://");

    /// <summary>TCP transport.</summary>
    public static readonly TransportLayer Tcp = new("tcp", "tcp://");

    public override string ToString() => Name;
}

/// <summary>
/// Builder for constructing transport protocols.
/// </summary>
public readonly record struct TransportBuilder
{
    public MessagingLibrary Library { get; }
    public MessagingPattern Pattern { get; }

    internal TransportBuilder(MessagingLibrary library, MessagingPattern pattern)
    {
        Library = library;
        Pattern = pattern;
    }

    public static TransportProtocol operator +(TransportBuilder builder, TransportLayer layer)
        => new(builder.Library, builder.Pattern, layer);

    public override string ToString() => $"{Library}+{Pattern}";
}

/// <summary>
/// Complete transport protocol specification.
/// </summary>
public readonly record struct TransportProtocol
{
    public MessagingLibrary Library { get; }
    public MessagingPattern Pattern { get; }
    public TransportLayer Layer { get; }

    internal TransportProtocol(MessagingLibrary library, MessagingPattern pattern, TransportLayer layer)
    {
        Library = library;
        Pattern = pattern;
        Layer = layer;
    }

    /// <summary>
    /// Protocol string for parsing (e.g., "nng+push+ipc").
    /// </summary>
    public string ProtocolString => $"{Library}+{Pattern}+{Layer}";

    /// <summary>
    /// Creates the NNG address from a path/host.
    /// </summary>
    public string CreateNngAddress(string pathOrHost) => Layer.UriPrefix + pathOrHost;

    /// <summary>
    /// Checks if this is a push pattern.
    /// </summary>
    public bool IsPush => Pattern == MessagingPattern.Push;

    /// <summary>
    /// Checks if this is a pub pattern.
    /// </summary>
    public bool IsPub => Pattern == MessagingPattern.Pub;

    public override string ToString() => ProtocolString;

    /// <summary>
    /// Parses a protocol string (e.g., "nng+push+ipc").
    /// </summary>
    public static TransportProtocol Parse(string s)
    {
        if (!TryParse(s, out var result))
            throw new FormatException($"Invalid transport protocol: {s}");
        return result;
    }

    /// <summary>
    /// Tries to parse a protocol string.
    /// </summary>
    public static bool TryParse(string? s, out TransportProtocol result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        var parts = s.Split('+');
        if (parts.Length != 3)
            return false;

        // Parse library
        MessagingLibrary library;
        if (parts[0].Equals("nng", StringComparison.OrdinalIgnoreCase))
            library = MessagingLibrary.Nng;
        else
            return false;

        // Parse pattern
        MessagingPattern pattern;
        if (parts[1].Equals("push", StringComparison.OrdinalIgnoreCase))
            pattern = MessagingPattern.Push;
        else if (parts[1].Equals("pull", StringComparison.OrdinalIgnoreCase))
            pattern = MessagingPattern.Pull;
        else if (parts[1].Equals("pub", StringComparison.OrdinalIgnoreCase))
            pattern = MessagingPattern.Pub;
        else if (parts[1].Equals("sub", StringComparison.OrdinalIgnoreCase))
            pattern = MessagingPattern.Sub;
        else
            return false;

        // Parse layer
        TransportLayer layer;
        if (parts[2].Equals("ipc", StringComparison.OrdinalIgnoreCase))
            layer = TransportLayer.Ipc;
        else if (parts[2].Equals("tcp", StringComparison.OrdinalIgnoreCase))
            layer = TransportLayer.Tcp;
        else
            return false;

        result = new TransportProtocol(library, pattern, layer);
        return true;
    }
}

/// <summary>
/// Static helpers for building transport protocols using + operator.
/// </summary>
public static class Transport
{
    /// <summary>NNG messaging library.</summary>
    public static MessagingLibrary Nng => MessagingLibrary.Nng;

    /// <summary>Push messaging pattern.</summary>
    public static MessagingPattern Push => MessagingPattern.Push;

    /// <summary>Pull messaging pattern.</summary>
    public static MessagingPattern Pull => MessagingPattern.Pull;

    /// <summary>Pub messaging pattern.</summary>
    public static MessagingPattern Pub => MessagingPattern.Pub;

    /// <summary>Sub messaging pattern.</summary>
    public static MessagingPattern Sub => MessagingPattern.Sub;

    /// <summary>IPC transport layer.</summary>
    public static TransportLayer Ipc => TransportLayer.Ipc;

    /// <summary>TCP transport layer.</summary>
    public static TransportLayer Tcp => TransportLayer.Tcp;

    /// <summary>File output (not a real transport).</summary>
    public static readonly string File = "file";
}
