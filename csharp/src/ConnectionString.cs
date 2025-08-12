using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

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

    /// <summary>
    /// Immutable connection string representation.
    /// </summary>
    public readonly record struct ConnectionString : IParsable<ConnectionString>
    {
        public Protocol Protocol { get; init; }
        public string? Host { get; init; }
        public int? Port { get; init; }
        public string Path { get; init; }
        public ImmutableDictionary<string, string> Parameters { get; init; }

        // Convenience properties for common parameters
        public string? BufferName => Protocol == Protocol.Shm ? Path : null;
        public long BufferSize => GetSizeParameter("buffer_size", 20 * 1024 * 1024); // Default 20MB
        public long MetadataSize => GetSizeParameter("metadata_size", 4 * 1024); // Default 4KB
        public string Mode => Parameters.GetValueOrDefault("mode", "duplex");

        private ConnectionString(Protocol protocol, string? host, int? port, string path, ImmutableDictionary<string, string> parameters)
        {
            Protocol = protocol;
            Host = host;
            Port = port;
            Path = path;
            Parameters = parameters;
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

            // Parse protocol://host:port/path?params
            var parts = s.Split("://", 2);
            if (parts.Length != 2)
                return false;

            var protocolString = parts[0].ToLower();
            var remainder = parts[1];
            
            // Parse protocol string to enum flags
            Protocol protocol;
            switch (protocolString)
            {
                case "shm":
                    protocol = Protocol.Shm;
                    break;
                case "mjpeg+http":
                    protocol = Protocol.Mjpeg | Protocol.Http;
                    break;
                case "mjpeg+tcp":
                    protocol = Protocol.Mjpeg | Protocol.Tcp;
                    break;
                default:
                    return false; // Unsupported protocol
            }
            
            var parameters = ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
            
            // Extract query parameters if present
            var queryIndex = remainder.IndexOf('?');
            if (queryIndex >= 0)
            {
                var queryString = remainder[(queryIndex + 1)..];
                remainder = remainder[..queryIndex];
                parameters = ParseQueryString(queryString);
            }

            string? host = null;
            int? port = null;
            string path;

            // Parse based on protocol
            if (protocol == Protocol.Shm)
            {
                // For shm://, the remainder is just the buffer name
                path = remainder;
            }
            else if (protocol.HasFlag(Protocol.Mjpeg))
            {
                // Parse host:port/path
                var pathIndex = remainder.IndexOf('/');
                var hostPort = pathIndex >= 0 ? remainder[..pathIndex] : remainder;
                path = pathIndex >= 0 ? remainder[(pathIndex + 1)..] : "";

                var colonIndex = hostPort.LastIndexOf(':');
                if (colonIndex >= 0)
                {
                    host = hostPort[..colonIndex];
                    if (int.TryParse(hostPort[(colonIndex + 1)..], out var parsedPort))
                    {
                        port = parsedPort;
                    }
                }
                else
                {
                    host = hostPort;
                }
            }
            else
            {
                return false; // Unsupported protocol
            }

            result = new ConnectionString(protocol, host, port, path, parameters);
            return true;
        }

        private static ImmutableDictionary<string, string> ParseQueryString(string queryString)
        {
            if (string.IsNullOrEmpty(queryString))
                return ImmutableDictionary<string, string>.Empty;

            var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
            
            var pairs = queryString.Split('&');
            foreach (var pair in pairs)
            {
                var keyValue = pair.Split('=', 2);
                if (keyValue.Length == 2)
                {
                    var key = Uri.UnescapeDataString(keyValue[0]);
                    var value = Uri.UnescapeDataString(keyValue[1]);
                    builder[key] = value;
                }
            }
            
            return builder.ToImmutable();
        }

        private readonly long GetSizeParameter(string key, long defaultValue)
        {
            if (!Parameters.TryGetValue(key, out var value))
                return defaultValue;

            value = value.ToUpperInvariant();
            
            // Parse size with units (B, KB, MB, GB)
            return value switch
            {
                _ when value.EndsWith("GB") => long.Parse(value[..^2]) * 1024L * 1024L * 1024L,
                _ when value.EndsWith("MB") => long.Parse(value[..^2]) * 1024L * 1024L,
                _ when value.EndsWith("KB") => long.Parse(value[..^2]) * 1024L,
                _ when value.EndsWith("B") => long.Parse(value[..^1]),
                _ => long.Parse(value) * 1024L * 1024L // No unit means MB by default
            };
        }

        public override string ToString()
        {
            string protocolString;
            if (Protocol == Protocol.Shm)
                protocolString = "shm";
            else if (Protocol == (Protocol.Mjpeg | Protocol.Http))
                protocolString = "mjpeg+http";
            else if (Protocol == (Protocol.Mjpeg | Protocol.Tcp))
                protocolString = "mjpeg+tcp";
            else
                protocolString = Protocol.ToString().ToLower();

            var baseUrl = Protocol == Protocol.Shm
                ? $"{protocolString}://{Path}"
                : $"{protocolString}://{Host}{(Port.HasValue ? $":{Port}" : "")}/{Path}";
            
            return Parameters.Any() 
                ? $"{baseUrl}?{string.Join("&", Parameters.Select(p => $"{p.Key}={p.Value}"))}"
                : baseUrl;
        }
    }
}