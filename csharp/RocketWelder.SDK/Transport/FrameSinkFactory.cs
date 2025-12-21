using System;
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
    /// </summary>
    /// <param name="protocol">The transport protocol</param>
    /// <param name="address">The address (socket path or NNG address)</param>
    /// <param name="logger">Optional logger for diagnostics</param>
    /// <returns>An IFrameSink connected to the specified address</returns>
    /// <exception cref="NotSupportedException">If protocol is not supported for sinks</exception>
    public static IFrameSink Create(TransportProtocol protocol, string address, ILogger? logger = null)
    {
        if (protocol.IsSocket)
        {
            logger?.LogInformation("Creating Unix socket frame sink at: {Path}", address);
            return UnixSocketFrameSink.Connect(address);
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
}
