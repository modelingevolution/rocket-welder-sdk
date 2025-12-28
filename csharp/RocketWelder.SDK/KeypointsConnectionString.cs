using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using RocketWelder.SDK.Internal;

namespace RocketWelder.SDK;

/// <summary>
/// Strongly-typed connection string for Keypoints output.
/// Format: protocol://path?param1=value1&amp;param2=value2
///
/// Supported protocols:
/// - file:///path/to/file.bin - File output (absolute path)
/// - file://relative/path.bin - File output (relative path)
/// - socket:///tmp/socket.sock - Unix domain socket
/// - nng+push+ipc://tmp/keypoints - NNG Push over IPC
/// - nng+push+tcp://host:port - NNG Push over TCP
/// - nng+pub+ipc://tmp/keypoints - NNG Pub over IPC
///
/// Supported parameters:
/// - masterFrameInterval: Interval between master frames (default: 300)
/// </summary>
public readonly record struct KeypointsConnectionString : IParsable<KeypointsConnectionString>
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
    /// Interval between master frames for delta encoding.
    /// </summary>
    public int MasterFrameInterval { get; }

    /// <summary>
    /// Additional parameters from the connection string.
    /// </summary>
    public IReadOnlyDictionary<string, string> Parameters { get; }

    private KeypointsConnectionString(
        string value,
        TransportProtocol protocol,
        string address,
        int masterFrameInterval,
        IReadOnlyDictionary<string, string> parameters)
    {
        Value = value;
        Protocol = protocol;
        Address = address;
        MasterFrameInterval = masterFrameInterval;
        Parameters = parameters;
    }

    /// <summary>
    /// Default connection string for Keypoints.
    /// </summary>
    public static KeypointsConnectionString Default => Parse("nng+push+ipc://tmp/rocket-welder-keypoints?masterFrameInterval=300", null);

    /// <summary>
    /// Creates a connection string from environment variable or uses default.
    /// </summary>
    public static KeypointsConnectionString FromEnvironment(string variableName = "KEYPOINTS_CONNECTION_STRING")
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return string.IsNullOrEmpty(value) ? Default : Parse(value, null);
    }

    public static KeypointsConnectionString Parse(string s, IFormatProvider? provider)
    {
        if (!TryParse(s, provider, out var result))
            throw new FormatException($"Invalid Keypoints connection string: {s}");
        return result;
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out KeypointsConnectionString result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ConnectionStringParser.ExtractQueryParameters(s, out var endpointPart, parameters);

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

        // Parse masterFrameInterval
        var masterFrameInterval = 300; // default
        if (parameters.TryGetValue("masterframeinterval", out var mfiStr) &&
            int.TryParse(mfiStr, out var mfi))
        {
            masterFrameInterval = mfi;
        }

        result = new KeypointsConnectionString(s, protocol, address, masterFrameInterval, parameters);
        return true;
    }

    public override string ToString() => Value;

    public static implicit operator string(KeypointsConnectionString cs) => cs.Value;
}
