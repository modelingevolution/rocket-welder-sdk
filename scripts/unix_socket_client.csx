#!/usr/bin/env dotnet-script

// C# Unix Socket Client - sends frames to Python server
// Usage: dotnet-script unix_socket_client.csx <socket_path> <message>

using System;
using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Threading;

var socketPath = Args.Count > 0 ? Args[0] : "/tmp/rocket-welder-cross-platform.sock";
var message = Args.Count > 1 ? Args[1] : "Hello from C# Unix Socket!";

Console.WriteLine($"[C# Client] Connecting to {socketPath}");

// Wait for server to be ready
for (int i = 0; i < 20; i++)
{
    if (File.Exists(socketPath))
        break;
    Console.WriteLine("[C# Client] Waiting for server...");
    Thread.Sleep(250);
}

if (!File.Exists(socketPath))
{
    Console.WriteLine("[C# Client] Server socket not found!");
    Environment.Exit(1);
}

try
{
    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    socket.Connect(new UnixDomainSocketEndPoint(socketPath));

    Console.WriteLine("[C# Client] Connected!");

    var stream = new NetworkStream(socket);

    // Prepare frame data
    var frameData = System.Text.Encoding.UTF8.GetBytes(message);

    // Write 4-byte length prefix (little-endian)
    var lengthBytes = new byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(lengthBytes, (uint)frameData.Length);
    stream.Write(lengthBytes, 0, 4);

    // Write frame data
    stream.Write(frameData, 0, frameData.Length);
    stream.Flush();

    Console.WriteLine($"[C# Client] Sent {frameData.Length} bytes: {message}");

    stream.Dispose();
    socket.Dispose();

    Console.WriteLine("[C# Client] Success!");
}
catch (Exception ex)
{
    Console.WriteLine($"[C# Client] Error: {ex.Message}");
    Environment.Exit(1);
}
