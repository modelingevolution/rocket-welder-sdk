using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace RocketWelder.SDK.HighLevel;

/// <summary>
/// Strongly-typed connection string for KeyPoints output.
/// Format: protocol:path?param1=value1&amp;param2=value2
///
/// Supported protocols (composable with + operator):
/// - Transport.Nng + Transport.Push + Transport.Ipc → nng+push+ipc:/path
/// - Transport.Nng + Transport.Push + Transport.Tcp → nng+push+tcp:host:port
/// - Transport.Nng + Transport.Pub + Transport.Ipc → nng+pub+ipc:/path
/// - file:/path/to/file.bin - File output
///
/// Supported parameters:
/// - masterFrameInterval: Interval between master frames (default: 300)
///
/// Example:
/// <code>
/// var protocol = Transport.Nng + Transport.Push + Transport.Ipc;
/// var cs = KeyPointsConnectionString.Parse("nng+push+ipc:/tmp/keypoints", null);
/// </code>
/// </summary>
public readonly record struct KeyPointsConnectionString : IParsable<KeyPointsConnectionString>
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
    /// The NNG address for NNG transports (e.g., "ipc:///tmp/keypoints", "tcp://localhost:5555").
    /// For file transport, this is the file path.
    /// </summary>
    public string Address { get; }

    /// <summary>
    /// Interval between master frames for delta encoding.
    /// </summary>
    public int MasterFrameInterval { get; }

    /// <summary>
    /// Additional parameters from the connection string.
    /// </summary>
    public IReadOnlyDictionary<string, string> Parameters { get; }

    private KeyPointsConnectionString(
        string value,
        TransportProtocol? protocol,
        bool isFile,
        string address,
        int masterFrameInterval,
        IReadOnlyDictionary<string, string> parameters)
    {
        Value = value;
        Protocol = protocol;
        IsFile = isFile;
        Address = address;
        MasterFrameInterval = masterFrameInterval;
        Parameters = parameters;
    }

    /// <summary>
    /// Default connection string for KeyPoints.
    /// </summary>
    public static KeyPointsConnectionString Default => Parse("nng+push+ipc:/tmp/rocket-welder-keypoints?masterFrameInterval=300", null);

    /// <summary>
    /// Creates a connection string from environment variable or uses default.
    /// </summary>
    public static KeyPointsConnectionString FromEnvironment(string variableName = "KEYPOINTS_CONNECTION_STRING")
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return string.IsNullOrEmpty(value) ? Default : Parse(value, null);
    }

    public static KeyPointsConnectionString Parse(string s, IFormatProvider? provider)
    {
        if (!TryParse(s, provider, out var result))
            throw new FormatException($"Invalid KeyPoints connection string: {s}");
        return result;
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out KeyPointsConnectionString result)
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

        // Parse masterFrameInterval
        var masterFrameInterval = 300; // default
        if (parameters.TryGetValue("masterframeinterval", out var mfiStr) &&
            int.TryParse(mfiStr, out var mfi))
        {
            masterFrameInterval = mfi;
        }

        result = new KeyPointsConnectionString(s, protocol, isFile, address, masterFrameInterval, parameters);
        return true;
    }

    public override string ToString() => Value;

    public static implicit operator string(KeyPointsConnectionString cs) => cs.Value;
}
