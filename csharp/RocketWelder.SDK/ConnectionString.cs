using System;
using System.Diagnostics.CodeAnalysis;
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
        Tcp = 1 << 3
    }

    public enum Mode
    {
        OneWay,
        Duplex
    }

    /// <summary>
    /// Connection string representation.
    /// </summary>
    [JsonConverter(typeof(JsonParsableConverter<ConnectionString>))]
    public readonly struct ConnectionString : IParsable<ConnectionString>
    {
        public Protocol Protocol { get; }
        public string? Host { get; }
        public int? Port { get; }
        public string? BufferName { get; }
        public Bytes BufferSize { get; }
        public Bytes MetadataSize { get; }
        public Mode Mode { get; }
        public int TimeoutMs { get; } = 5000; // Default timeout for connections

        private ConnectionString(
            Protocol protocol,
            string? host = null,
            int? port = null,
            string? bufferName = null,
            Bytes bufferSize = default,
            Bytes metadataSize = default,
            Mode mode = Mode.OneWay)
        {
            Protocol = protocol;
            Host = host;
            Port = port;
            BufferName = bufferName;
            BufferSize = bufferSize == default ? "256MB" : bufferSize;
            MetadataSize = metadataSize == default ? "4KB" : metadataSize;
            Mode = mode;
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
                    _ => Protocol.None
                };
                
                if (protocol == Protocol.None)
                    return false;
            }

            string? host = null;
            int? port = null;
            string? bufferName = null;
            Bytes bufferSize = default;
            Bytes metadataSize = default;
            Mode mode = Mode.OneWay;

            // Extract query parameters if present
            var queryIndex = remainder.IndexOf('?');
            if (queryIndex >= 0)
            {
                var queryString = remainder[(queryIndex + 1)..];
                remainder = remainder[..queryIndex];
                
                // Parse simple parameters for SHM
                var pairs = queryString.Split('&');
                foreach (var pair in pairs)
                {
                    var keyValue = pair.Split('=', 2);
                    if (keyValue.Length == 2)
                    {
                        var key = keyValue[0].ToLowerInvariant();
                        var value = keyValue[1];
                        
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
                                if (Enum.TryParse<Mode>(value, true, out var m))
                                    mode = m;
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
                bufferSize,
                metadataSize,
                mode);
            return true;
        }

        public override string ToString()
        {
            var protocolString = Protocol.ToFlagsString("+").ToLowerInvariant();

            if (Protocol == Protocol.Shm)
            {
                return $"{protocolString}://{BufferName}?size={BufferSize}&metadata={MetadataSize}&mode={Mode}";
            }
            else
            {
                return $"{protocolString}://{Host}:{Port}";
            }
        }
    }
}