#!/usr/bin/env dotnet-script
#r "nuget: ModelingEvolution.Nng, 1.0.2"

// C# Segmentation Writer - writes segmentation data over NNG
// Usage: dotnet-script segmentation_writer.csx <ipc_address> <output_file>
// Writes a single segmentation frame with test data

using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using nng;
using nng.Factories.Latest;

var address = Args.Count > 0 ? Args[0] : "ipc:///tmp/rocket-welder-segmentation-test";
var outputFile = Args.Count > 1 ? Args[1] : "/tmp/rocket-welder-test/csharp_segmentation_written.txt";

Console.WriteLine($"[C# Segmentation Writer] Binding to {address}");

try
{
    var factory = new Factory();
    var socket = factory.PusherOpen().Unwrap();
    socket.Listen(address).Unwrap();

    Console.WriteLine("[C# Segmentation Writer] Bound, waiting for connection...");
    Thread.Sleep(500);

    // Build segmentation frame manually (matching SDK format)
    using var buffer = new MemoryStream();

    // Frame ID (8 bytes, little-endian)
    var frameIdBytes = new byte[8];
    BinaryPrimitives.WriteUInt64LittleEndian(frameIdBytes, 123UL);
    buffer.Write(frameIdBytes, 0, 8);

    // Width and Height (varints)
    WriteVarint(buffer, 1920);  // Width
    WriteVarint(buffer, 1080);  // Height

    // Instance 1: class_id=1, instance_id=1, 4 points forming a rectangle
    buffer.WriteByte(1);  // class_id
    buffer.WriteByte(1);  // instance_id
    WriteVarint(buffer, 4);  // point count

    // Points with delta encoding: first absolute (zigzag), rest delta (zigzag)
    // Point 0: (100, 100)
    WriteVarint(buffer, ZigZagEncode(100));
    WriteVarint(buffer, ZigZagEncode(100));
    // Point 1: (200, 100) -> delta (100, 0)
    WriteVarint(buffer, ZigZagEncode(100));
    WriteVarint(buffer, ZigZagEncode(0));
    // Point 2: (200, 200) -> delta (0, 100)
    WriteVarint(buffer, ZigZagEncode(0));
    WriteVarint(buffer, ZigZagEncode(100));
    // Point 3: (100, 200) -> delta (-100, 0)
    WriteVarint(buffer, ZigZagEncode(-100));
    WriteVarint(buffer, ZigZagEncode(0));

    // Instance 2: class_id=2, instance_id=1, 3 points forming a triangle
    buffer.WriteByte(2);  // class_id
    buffer.WriteByte(1);  // instance_id
    WriteVarint(buffer, 3);  // point count

    // Point 0: (300, 300)
    WriteVarint(buffer, ZigZagEncode(300));
    WriteVarint(buffer, ZigZagEncode(300));
    // Point 1: (350, 250) -> delta (50, -50)
    WriteVarint(buffer, ZigZagEncode(50));
    WriteVarint(buffer, ZigZagEncode(-50));
    // Point 2: (400, 300) -> delta (50, 50)
    WriteVarint(buffer, ZigZagEncode(50));
    WriteVarint(buffer, ZigZagEncode(50));

    var frameData = buffer.ToArray();
    Console.WriteLine($"[C# Segmentation Writer] Sending {frameData.Length} bytes");

    socket.Send(frameData).Unwrap();

    // Write result
    Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
    File.WriteAllText(outputFile, $"written: frame_id=123, width=1920, height=1080, instances=2, bytes={frameData.Length}");

    Console.WriteLine("[C# Segmentation Writer] Sent successfully!");
    Thread.Sleep(100);

    socket.Dispose();
}
catch (Exception ex)
{
    Console.WriteLine($"[C# Segmentation Writer] Error: {ex.Message}");
    Environment.Exit(1);
}

void WriteVarint(System.IO.Stream stream, uint value)
{
    while (value >= 0x80)
    {
        stream.WriteByte((byte)(value | 0x80));
        value >>= 7;
    }
    stream.WriteByte((byte)value);
}

uint ZigZagEncode(int value)
{
    return (uint)((value << 1) ^ (value >> 31));
}
