#!/usr/bin/env dotnet-script

// C# TCP Server - receives frames from Python client
// Usage: dotnet-script tcp_server.csx <port> <output_file>

using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;

var port = Args.Count > 0 ? int.Parse(Args[0]) : 5555;
var outputFile = Args.Count > 1 ? Args[1] : "/tmp/rocket-welder-test/csharp_tcp_received.txt";

Console.WriteLine($"[C# TCP Server] Binding to port {port}");

try
{
    var listener = new TcpListener(IPAddress.Loopback, port);
    listener.Start();

    Console.WriteLine("[C# TCP Server] Listening...");

    // Set accept timeout
    var acceptTask = listener.AcceptTcpClientAsync();
    if (!acceptTask.Wait(10000))
    {
        Console.WriteLine("[C# TCP Server] Accept timeout");
        File.WriteAllText(outputFile, "error: accept timeout");
        return;
    }

    var client = acceptTask.Result;
    Console.WriteLine("[C# TCP Server] Client connected!");

    var stream = client.GetStream();
    stream.ReadTimeout = 5000;

    // Read 4-byte length prefix (little-endian)
    var lengthBytes = new byte[4];
    var bytesRead = stream.Read(lengthBytes, 0, 4);
    if (bytesRead < 4)
    {
        Console.WriteLine("[C# TCP Server] Failed to read length prefix");
        File.WriteAllText(outputFile, "error: incomplete length prefix");
        return;
    }

    var frameLength = BinaryPrimitives.ReadUInt32LittleEndian(lengthBytes);
    Console.WriteLine($"[C# TCP Server] Frame length: {frameLength}");

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
    Console.WriteLine($"[C# TCP Server] Received {totalRead} bytes: {text}");

    // Write result
    Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
    File.WriteAllText(outputFile, $"received: {totalRead} bytes, content: {text}");

    stream.Dispose();
    client.Dispose();
    listener.Stop();

    Console.WriteLine("[C# TCP Server] Success!");
}
catch (Exception ex)
{
    Console.WriteLine($"[C# TCP Server] Error: {ex.Message}");
    File.WriteAllText(outputFile, $"error: {ex.Message}");
}
