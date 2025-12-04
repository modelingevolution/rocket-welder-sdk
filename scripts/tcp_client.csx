#!/usr/bin/env dotnet-script

// C# TCP Client - sends frames to Python server
// Usage: dotnet-script tcp_client.csx <port> <message>

using System;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Threading;

var port = Args.Count > 0 ? int.Parse(Args[0]) : 5555;
var message = Args.Count > 1 ? Args[1] : "Hello from C# TCP!";

Console.WriteLine($"[C# TCP Client] Connecting to 127.0.0.1:{port}");

#nullable enable
try
{
    TcpClient? client = null;

    // Retry connection with exponential backoff
    for (int i = 0; i < 20; i++)
    {
        try
        {
            client = new TcpClient();
            client.Connect("127.0.0.1", port);
            break; // Connected successfully
        }
        catch (SocketException)
        {
            client?.Dispose();
            client = null;
            Console.WriteLine($"[C# TCP Client] Waiting for server... (attempt {i + 1})");
            Thread.Sleep(250);
        }
    }

    if (client == null)
    {
        Console.WriteLine("[C# TCP Client] Failed to connect after 20 attempts");
        Environment.Exit(1);
    }

    Console.WriteLine("[C# TCP Client] Connected!");

    var stream = client.GetStream();

    // Prepare frame data
    var frameData = System.Text.Encoding.UTF8.GetBytes(message);

    // Write 4-byte length prefix (little-endian)
    var lengthBytes = new byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(lengthBytes, (uint)frameData.Length);
    stream.Write(lengthBytes, 0, 4);

    // Write frame data
    stream.Write(frameData, 0, frameData.Length);
    stream.Flush();

    Console.WriteLine($"[C# TCP Client] Sent {frameData.Length} bytes: {message}");

    stream.Dispose();
    client.Dispose();

    Console.WriteLine("[C# TCP Client] Success!");
}
catch (Exception ex)
{
    Console.WriteLine($"[C# TCP Client] Error: {ex.Message}");
    Environment.Exit(1);
}
