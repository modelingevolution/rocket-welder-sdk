#!/usr/bin/env dotnet-script

// C# Unix Socket Server - receives frames from Python client
// Usage: dotnet-script unix_socket_server.csx <socket_path> <output_file>

using System;
using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Threading;

var socketPath = Args.Count > 0 ? Args[0] : "/tmp/rocket-welder-cross-platform.sock";
var outputFile = Args.Count > 1 ? Args[1] : "/tmp/rocket-welder-test/csharp_unix_received.txt";

Console.WriteLine($"[C# Server] Binding to {socketPath}");

try
{
    // Clean up existing socket
    if (File.Exists(socketPath))
        File.Delete(socketPath);

    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    socket.Bind(new UnixDomainSocketEndPoint(socketPath));
    socket.Listen(1);

    Console.WriteLine("[C# Server] Listening...");

    // Set accept timeout
    socket.ReceiveTimeout = 10000;

    var client = socket.Accept();
    Console.WriteLine("[C# Server] Client connected!");

    var stream = new NetworkStream(client);

    // Read 4-byte length prefix
    var lengthBytes = new byte[4];
    var bytesRead = stream.Read(lengthBytes, 0, 4);
    if (bytesRead < 4)
    {
        Console.WriteLine("[C# Server] Failed to read length prefix");
        File.WriteAllText(outputFile, "error: incomplete length prefix");
        return;
    }

    var frameLength = BinaryPrimitives.ReadUInt32LittleEndian(lengthBytes);
    Console.WriteLine($"[C# Server] Frame length: {frameLength}");

    // Read frame data
    var frameData = new byte[frameLength];
    var totalRead = 0;
    while (totalRead < frameLength)
    {
        bytesRead = stream.Read(frameData, totalRead, (int)frameLength - totalRead);
        if (bytesRead == 0) break;
        totalRead += bytesRead;
    }

    var text = System.Text.Encoding.UTF8.GetString(frameData);
    Console.WriteLine($"[C# Server] Received {totalRead} bytes: {text}");

    // Write result
    Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
    File.WriteAllText(outputFile, $"received: {totalRead} bytes, content: {text}");

    stream.Dispose();
    client.Dispose();
    socket.Dispose();

    // Clean up socket file
    if (File.Exists(socketPath))
        File.Delete(socketPath);

    Console.WriteLine("[C# Server] Success!");
}
catch (Exception ex)
{
    Console.WriteLine($"[C# Server] Error: {ex.Message}");
    File.WriteAllText(outputFile, $"error: {ex.Message}");
}
