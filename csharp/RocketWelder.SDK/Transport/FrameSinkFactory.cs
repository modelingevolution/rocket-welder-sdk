using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace RocketWelder.SDK.Transport;

/// <summary>
/// Factory for creating IFrameSink instances from parsed protocol and address.
/// Does NOT parse URLs - use SegmentationConnectionString or KeyPointsConnectionString for parsing.
/// </summary>
public static class FrameSinkFactory
{
    /// <summary>
    /// Creates a frame sink from parsed protocol and address.
    /// Returns NullFrameSink if protocol is default (no URL specified).
    /// </summary>
    /// <param name="protocol">The transport protocol</param>
    /// <param name="address">The address (file path, socket path, or NNG address)</param>
    /// <param name="logger">Optional logger for diagnostics</param>
    /// <returns>An IFrameSink connected to the specified address, or NullFrameSink if protocol is default</returns>
    /// <exception cref="NotSupportedException">If protocol is not supported for sinks</exception>
    public static IFrameSink Create(TransportProtocol protocol, string address, ILogger? logger = null)
    {
        // Handle null/default protocol - return null sink
        if (protocol == default || string.IsNullOrEmpty(protocol.Schema))
        {
            logger?.LogDebug("No protocol specified, using NullFrameSink");
            return NullFrameSink.Instance;
        }

        if (protocol.IsFile)
        {
            logger?.LogInformation("Creating file frame sink at: {Path}", address);
            var stream = new FileStream(address, FileMode.Create, FileAccess.Write, FileShare.Read);
            return new StreamFrameSink(stream, leaveOpen: false);
        }

        if (protocol.IsSocket)
        {
            logger?.LogInformation("Creating Unix socket server at: {Path}", address);
            return UnixSocketFrameSink.Bind(address);
        }

        if (protocol.IsNng)
        {
            logger?.LogInformation("Creating NNG frame sink ({Protocol}) at: {Address}", protocol.Schema, address);

            if (protocol.IsPub)
                return NngFrameSink.CreatePublisher(address);
            if (protocol.IsPush)
                return NngFrameSink.CreatePusher(address);

            throw new NotSupportedException(
                $"NNG protocol '{protocol.Schema}' is not supported for sinks (only pub and push are supported)");
        }

        throw new NotSupportedException(
            $"Transport protocol '{protocol.Schema}' is not supported for frame sinks");
    }

    /// <summary>
    /// Creates a null frame sink that discards all data.
    /// Use when no output URL is configured.
    /// </summary>
    public static IFrameSink CreateNull() => NullFrameSink.Instance;
}
