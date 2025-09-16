using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelingEvolution.JsonParsableConverter;

namespace RocketWelder.SDK
{
    /// <summary>
    /// Protocol flags for connection types.
    /// </summary>
    [Flags]
    public enum Protocol
    {
        None = 0,
        Shm = 1 << 0,
        Mjpeg = 1 << 1,
        Http = 1 << 2,
        Tcp = 1 << 3,
        File = 1 << 4
    }

    public enum ConnectionMode
    {
        OneWay,
        Duplex
    }

    /// <summary>
    /// Connection string representation.
    /// </summary>
    [JsonConverter(typeof(JsonParsableConverter<ConnectionString>))]
    public readonly record struct ConnectionString : IParsable<ConnectionString>
    {
        public Protocol Protocol { get;  }
        public string? Host { get; }
        public int? Port { get; }
        public string? BufferName { get; init; }
        public string? FilePath { get; init; }
        public Dictionary<string, string> Parameters { get; init; } = new();
        public long BufferSize { get; init; }
        public long MetadataSize { get; init; }
        public ConnectionMode ConnectionMode { get; init; }
        public int TimeoutMs { get; init; } = 5000; // Default timeout for connections

        private ConnectionString(
            Protocol protocol,
            string? host = null,
            int? port = null,
            string? bufferName = null,
            string? filePath = null,
            Dictionary<string, string>? parameters = null,
            long bufferSize = default,
            long metadataSize = default,
            ConnectionMode connectionMode = ConnectionMode.OneWay,
            TimeSpan? timeout = null)
        {
            Protocol = protocol;
            Host = host;
            Port = port;
            BufferName = bufferName;
            FilePath = filePath;
            Parameters = parameters ?? new Dictionary<string, string>();
            BufferSize = bufferSize == default ? (Bytes)"256MB" : bufferSize;
            MetadataSize = metadataSize == default ? (Bytes)"4KB" : metadataSize;
            ConnectionMode = connectionMode;
            TimeoutMs = timeout.HasValue ? (int)timeout.Value.TotalMilliseconds : 5000;
        }

        /// <summary>
        /// Creates a ConnectionString for shared memory (SHM) protocol.
        /// </summary>
        /// <param name="bufferName">Name of the shared memory buffer</param>
        /// <param name="bufferSize">Size of the buffer (default: 256MB)</param>
        /// <param name="metadataSize">Size of metadata (default: 4KB)</param>
        /// <param name="connectionMode">Connection mode (default: OneWay)</param>
        /// <returns>A ConnectionString configured for SHM</returns>
        public static ConnectionString CreateShm(
            string bufferName,
            long? bufferSize = null,
            long? metadataSize = null,
            ConnectionMode connectionMode = ConnectionMode.OneWay, TimeSpan? timeout=null)
        {
            return new ConnectionString(
                Protocol.Shm,
                host: null,
                port: null,
                bufferName: bufferName,
                bufferSize: bufferSize ?? (Bytes)"256MB",
                metadataSize: metadataSize ?? (Bytes)"4KB",
                connectionMode: connectionMode, timeout);
        }

        /// <summary>
        /// Creates a ConnectionString for MJPEG streaming over TCP or HTTP.
        /// </summary>
        /// <param name="host">Host address</param>
        /// <param name="port">Port number</param>
        /// <param name="withHttp">If true, uses HTTP+MJPEG; if false, uses TCP+MJPEG (default: false)</param>
        /// <param name="connectionMode">Connection mode (default: OneWay)</param>
        /// <returns>A ConnectionString configured for MJPEG streaming</returns>
        public static ConnectionString CreateMjpeg(
            string host,
            int port,
            bool withHttp = false,
            ConnectionMode connectionMode = ConnectionMode.OneWay, TimeSpan? timeout = null)
        {
            var protocol = withHttp
                ? Protocol.Http | Protocol.Mjpeg
                : Protocol.Tcp | Protocol.Mjpeg;

            return new ConnectionString(
                protocol,
                host: host,
                port: port,
                bufferName: null,
                filePath: null,
                parameters: null,
                bufferSize: default,
                metadataSize: default,
                connectionMode: connectionMode, timeout);
        }

        /// <summary>
        /// Creates a ConnectionString for file playback.
        /// </summary>
        /// <param name="filePath">Path to the video file</param>
        /// <param name="loop">If true, loops the video playback</param>
        /// <param name="connectionMode">Connection mode (default: OneWay)</param>
        /// <returns>A ConnectionString configured for file playback</returns>
        public static ConnectionString CreateFile(
            string filePath,
            bool loop = false,
            ConnectionMode connectionMode = ConnectionMode.OneWay,
            TimeSpan? timeout = null)
        {
            var parameters = new Dictionary<string, string>();
            if (loop)
                parameters["loop"] = "true";

            return new ConnectionString(
                Protocol.File,
                host: null,
                port: null,
                bufferName: null,
                filePath: filePath,
                parameters: parameters,
                bufferSize: default,
                metadataSize: default,
                connectionMode: connectionMode,
                timeout);
        }

        public static ConnectionString Parse(string s, IFormatProvider? provider = null)
        {
            if (TryParse(s, provider, out var result))
                return result;
            
            throw new FormatException($"Invalid connection string format: {s}");
        }

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out ConnectionString result)
        {
            result = default;
            
            if (string.IsNullOrWhiteSpace(s))
                return false;

            // Parse protocol://[host:port/]name?params
            var parts = s.Split("://", 2);
            if (parts.Length != 2)
                return false;

            var protocolString = parts[0].ToLowerInvariant();
            var remainder = parts[1];

            // Parse protocol using our EnumExtensions
            if (!EnumExtensions.TryParseFlags<Protocol>(protocolString, true, out var protocol))
            {
                // Handle special cases
                protocol = protocolString switch
                {
                    "shm" => Protocol.Shm,
                    "file" => Protocol.File,
                    _ => Protocol.None
                };

                if (protocol == Protocol.None)
                    return false;
            }

            string? host = null;
            int? port = null;
            string? bufferName = null;
            string? filePath = null;
            var parameters = new Dictionary<string, string>();
            Bytes bufferSize = default;
            Bytes metadataSize = default;
            TimeSpan timeout = TimeSpan.FromMilliseconds(5000);
            ConnectionMode connectionMode = ConnectionMode.OneWay;

            // Extract query parameters if present
            var queryIndex = remainder.IndexOf('?');
            if (queryIndex >= 0)
            {
                var queryString = remainder[(queryIndex + 1)..];
                remainder = remainder[..queryIndex];

                // Parse parameters
                var pairs = queryString.Split('&');
                foreach (var pair in pairs)
                {
                    var keyValue = pair.Split('=', 2);
                    if (keyValue.Length == 2)
                    {
                        var key = keyValue[0].ToLowerInvariant();
                        var value = keyValue[1];

                        // Store all parameters for controllers to use
                        parameters[key] = value;

                        switch (key)
                        {
                            case "size":
                                if (Bytes.TryParse(value, null, out var size))
                                    bufferSize = size;
                                break;
                            case "metadata":
                                if (Bytes.TryParse(value, null, out var metadata))
                                    metadataSize = metadata;
                                break;
                            case "mode":
                                if (Enum.TryParse<ConnectionMode>(value, true, out var m))
                                    connectionMode = m;
                                break;
                            case "timeout":
                                if (int.TryParse(value, out var timeout_ms))
                                    timeout = TimeSpan.FromMilliseconds(timeout_ms);
                                break;
                        }
                    }
                }
            }

            // Parse based on protocol
            if (protocol == Protocol.Shm)
            {
                // For shm://, the remainder is just the buffer name
                bufferName = remainder;
            }
            else if (protocol == Protocol.File)
            {
                // For file://, the remainder is the file path
                // Handle both file:///absolute/path and file://relative/path
                filePath = remainder.StartsWith("/") ? remainder : "/" + remainder;
            }
            else if (protocol.HasFlag(Protocol.Mjpeg))
            {
                // Parse host:port
                var colonIndex = remainder.LastIndexOf(':');
                if (colonIndex >= 0)
                {
                    host = remainder[..colonIndex];
                    if (int.TryParse(remainder[(colonIndex + 1)..], out var parsedPort))
                    {
                        port = parsedPort;
                    }
                }
                else
                {
                    host = remainder;
                    // Default ports
                    port = protocol.HasFlag(Protocol.Http) ? 80 : 8080;
                }
            }
            else
            {
                return false;
            }

            result = new ConnectionString(
                protocol,
                host,
                port,
                bufferName,
                filePath,
                parameters,
                bufferSize,
                metadataSize,
                connectionMode, timeout);
            return true;
        }

        public override string ToString()
        {
            var protocolString = Protocol.ToFlagsString("+").ToLowerInvariant();

            if (Protocol == Protocol.Shm)
                return $"{protocolString}://{BufferName}?size={(Bytes)BufferSize}&metadata={(Bytes)MetadataSize}&mode={ConnectionMode}&timeout={TimeoutMs}";
            else if (Protocol == Protocol.File)
            {
                var queryString = Parameters.Count > 0
                    ? "?" + string.Join("&", Parameters.Select(p => $"{p.Key}={p.Value}"))
                    : "";
                return $"{protocolString}://{FilePath}{queryString}";
            }
            else
                return $"{protocolString}://{Host}:{Port}";
        }
    }
}