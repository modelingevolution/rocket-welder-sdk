using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace RocketWelder.SDK.HighLevel;

/// <summary>
/// Strongly-typed connection string for Segmentation output.
/// Format: protocol:path?param1=value1&amp;param2=value2
///
/// Supported protocols (composable with + operator):
/// - Transport.Nng + Transport.Push + Transport.Ipc → nng+push+ipc:/path
/// - Transport.Nng + Transport.Push + Transport.Tcp → nng+push+tcp:host:port
/// - Transport.Nng + Transport.Pub + Transport.Ipc → nng+pub+ipc:/path
/// - file:/path/to/file.bin - File output
///
/// Example:
/// <code>
/// var protocol = Transport.Nng + Transport.Push + Transport.Ipc;
/// var cs = SegmentationConnectionString.Parse("nng+push+ipc:/tmp/segmentation", null);
/// </code>
/// </summary>
public readonly record struct SegmentationConnectionString : IParsable<SegmentationConnectionString>
{
    /// <summary>
    /// The full original connection string.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// The transport protocol (null for file transport).
    /// </summary>
    public TransportProtocol? Protocol { get; }

    /// <summary>
    /// True if this is a file transport (not NNG).
    /// </summary>
    public bool IsFile { get; }

    /// <summary>
    /// The NNG address for NNG transports (e.g., "ipc:///tmp/segmentation", "tcp://localhost:5556").
    /// For file transport, this is the file path.
    /// </summary>
    public string Address { get; }

    /// <summary>
    /// Additional parameters from the connection string.
    /// </summary>
    public IReadOnlyDictionary<string, string> Parameters { get; }

    private SegmentationConnectionString(
        string value,
        TransportProtocol? protocol,
        bool isFile,
        string address,
        IReadOnlyDictionary<string, string> parameters)
    {
        Value = value;
        Protocol = protocol;
        IsFile = isFile;
        Address = address;
        Parameters = parameters;
    }

    /// <summary>
    /// Default connection string for Segmentation.
    /// </summary>
    public static SegmentationConnectionString Default => Parse("nng+push+ipc:/tmp/rocket-welder-segmentation", null);

    /// <summary>
    /// Creates a connection string from environment variable or uses default.
    /// </summary>
    public static SegmentationConnectionString FromEnvironment(string variableName = "SEGMENTATION_CONNECTION_STRING")
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return string.IsNullOrEmpty(value) ? Default : Parse(value, null);
    }

    public static SegmentationConnectionString Parse(string s, IFormatProvider? provider)
    {
        if (!TryParse(s, provider, out var result))
            throw new FormatException($"Invalid Segmentation connection string: {s}");
        return result;
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out SegmentationConnectionString result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Extract query parameters
        var queryIndex = s.IndexOf('?');
        string endpointPart = s;
        if (queryIndex >= 0)
        {
            var queryString = s[(queryIndex + 1)..];
            endpointPart = s[..queryIndex];

            foreach (var pair in queryString.Split('&'))
            {
                var keyValue = pair.Split('=', 2);
                if (keyValue.Length == 2)
                    parameters[keyValue[0].ToLowerInvariant()] = keyValue[1];
            }
        }

        // Parse protocol and address
        // Format: protocol:path (e.g., nng+push+ipc:/tmp/foo)
        TransportProtocol? protocol = null;
        bool isFile = false;
        string address;

        var colonIndex = endpointPart.IndexOf(':');
        if (colonIndex > 0 && !endpointPart.StartsWith("/"))
        {
            var protocolStr = endpointPart[..colonIndex];
            var pathPart = endpointPart[(colonIndex + 1)..];

            if (protocolStr.Equals("file", StringComparison.OrdinalIgnoreCase))
            {
                isFile = true;
                address = pathPart;
            }
            else if (TransportProtocol.TryParse(protocolStr, out var parsed))
            {
                protocol = parsed;
                address = parsed.CreateNngAddress(pathPart);
            }
            else
            {
                return false;
            }
        }
        else
        {
            // Assume file path
            isFile = true;
            address = endpointPart;
        }

        result = new SegmentationConnectionString(s, protocol, isFile, address, parameters);
        return true;
    }

    public override string ToString() => Value;

    public static implicit operator string(SegmentationConnectionString cs) => cs.Value;
}
