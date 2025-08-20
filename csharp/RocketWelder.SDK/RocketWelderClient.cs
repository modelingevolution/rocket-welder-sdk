using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using System.Collections.Concurrent;
using Emgu.CV;
using ZeroBuffer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Text;
using System.Net.Http;
using System.IO;
using System.Net.Sockets;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Diagnostics;

namespace RocketWelder.SDK
{
    // NO MEMORY COPY! NO FUCKING MEMORY COPY!
    // NO MEMORY ALLOCATIONS IN THE MAIN LOOP! NO FUCKING MEMORY ALLOCATIONS!
    // NO BRANCHING IN THE MAIN LOOP! NO FUCKING CONDITIONAL BRANCHING CHECKS! (Action<Mat> or Action<Mat, Mat>)
    interface IController
    {
        void Start(Action<Mat, Mat> onFrame, CancellationToken cancellationToken = default);
        void Start(Action<Mat> onFrame, CancellationToken cancellationToken = default);
        void Stop(CancellationToken cancellationToken = default);
        void Dispose();
    }
    internal static class ControllerFactory
    {
        public static IController Create(in ConnectionString cs)
        {
            // Only based on cs;
        }
    }

    /// <summary>
    /// Main client for connecting to RocketWelder video streams.
    /// Supports multiple protocols: ZeroBuffer (shared memory), MJPEG over HTTP, and MJPEG over TCP.
    /// </summary>
    public class RocketWelderClient : IDisposable
    {
        /// <summary>
        /// Gets the connection configuration.
        /// </summary>
        public ConnectionString Connection { get; }
        
        /// <summary>
        /// Gets whether the client is currently running.
        /// </summary>
        public bool IsRunning { get; }
        
        
        
        /// <summary>
        /// Gets the metadata from the stream (if available).
        /// </summary>
        public GstMetadata? Metadata { get; }
        
        
        
        /// <summary>
        /// Creates a client from command line arguments and environment variables.
        /// Environment variable CONNECTION_STRING is checked first, then overridden by args.
        /// </summary>
        public static RocketWelderClient From(string[] args)
        {
            throw new NotImplementedException();
        }
        
        /// <summary>
        /// Creates a client from IConfiguration.
        /// Looks for "RocketWelder:ConnectionString" in configuration.
        /// </summary>
        public static RocketWelderClient From(IConfiguration configuration)
        {
            throw new NotImplementedException();
        }
        
        /// <summary>
        /// Creates a client from IConfiguration with logger.
        /// Looks for "RocketWelder:ConnectionString" in configuration.
        /// </summary>
        public static RocketWelderClient From(IConfiguration configuration, ILogger<RocketWelderClient>? logger)
        {
            throw new NotImplementedException();
        }
        
        /// <summary>
        /// Creates a client from environment variable CONNECTION_STRING.
        /// </summary>
        public static RocketWelderClient FromEnvironment()
        {
            throw new NotImplementedException();
        }
        
        /// <summary>
        /// Creates a client from environment variable CONNECTION_STRING with logger.
        /// </summary>
        public static RocketWelderClient FromEnvironment(ILogger<RocketWelderClient> logger)
        {
            throw new NotImplementedException();
        }
        
        /// <summary>
        /// Creates a client from a specific connection string.
        /// </summary>
        public static RocketWelderClient FromConnectionString(string connectionString)
        {
            throw new NotImplementedException();
        }
        
        /// <summary>
        /// Creates a client from a specific connection string with logger.
        /// </summary>
        public static RocketWelderClient FromConnectionString(string connectionString, ILogger<RocketWelderClient> logger)
        {
            throw new NotImplementedException();
        }


        /// <summary>
        /// Starts receiving frames from the video stream.
        /// </summary>
        public void Start(Action<Mat, Mat> OnFrame, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Starts receiving frames from the video stream.
        /// </summary>
        public void Start(Action<Mat> OnFrame, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
        
        /// <summary>
        /// Stops receiving frames and disconnects from the stream.
        /// </summary>
        public void Stop(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
        
        
        
        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}