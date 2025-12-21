using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace RocketWelder.SDK;

/// <summary>
/// Strongly-typed connection string for Segmentation output.
/// Format: protocol://path?param1=value1&amp;param2=value2
///
/// Supported protocols:
/// - file:///path/to/file.bin - File output (absolute path)
/// - file://relative/path.bin - File output (relative path)
/// - socket:///tmp/socket.sock - Unix domain socket
/// - nng+push+ipc://tmp/segmentation - NNG Push over IPC
/// - nng+push+tcp://host:port - NNG Push over TCP
/// - nng+pub+ipc://tmp/segmentation - NNG Pub over IPC
/// </summary>
public readonly record struct SegmentationConnectionString : IParsable<SegmentationConnectionString>
{
    /// <summary>
    /// The full original connection string.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// The transport protocol.
    /// </summary>
    public TransportProtocol Protocol { get; }

    /// <summary>
    /// The address (file path, socket path, or NNG address).
    /// </summary>
    public string Address { get; }

    /// <summary>
    /// Additional parameters from the connection string.
    /// </summary>
    public IReadOnlyDictionary<string, string> Parameters { get; }

    private SegmentationConnectionString(
        string value,
        TransportProtocol protocol,
        string address,
        IReadOnlyDictionary<string, string> parameters)
    {
        Value = value;
        Protocol = protocol;
        Address = address;
        Parameters = parameters;
    }

    /// <summary>
    /// Default connection string for Segmentation.
    /// </summary>
    public static SegmentationConnectionString Default => Parse("nng+push+ipc://tmp/rocket-welder-segmentation", null);

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
        // Format: protocol://path (e.g., nng+push+ipc://tmp/foo, file:///path, socket:///tmp/sock)
        var schemeEnd = endpointPart.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd <= 0)
            return false;

        var schemaStr = endpointPart[..schemeEnd];
        var pathPart = endpointPart[(schemeEnd + 3)..]; // skip "://"

        if (!TransportProtocol.TryParse(schemaStr, out var protocol))
            return false;

        // Build address based on protocol type
        string address;
        if (protocol.IsFile)
        {
            // file:///absolute/path → /absolute/path
            // file://relative/path → relative/path
            address = pathPart.StartsWith("/") ? pathPart : "/" + pathPart;
        }
        else if (protocol.IsSocket)
        {
            // socket:///tmp/sock → /tmp/sock
            address = pathPart.StartsWith("/") ? pathPart : "/" + pathPart;
        }
        else if (protocol.IsNng)
        {
            // NNG protocols need proper address format
            address = protocol.CreateNngAddress(pathPart);
        }
        else
        {
            return false;
        }

        result = new SegmentationConnectionString(s, protocol, address, parameters);
        return true;
    }

    public override string ToString() => Value;

    public static implicit operator string(SegmentationConnectionString cs) => cs.Value;
}
