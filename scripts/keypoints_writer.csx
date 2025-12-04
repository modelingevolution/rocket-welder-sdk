#!/usr/bin/env dotnet-script
#r "nuget: ModelingEvolution.Nng, 1.0.2"

// C# KeyPoints Writer - writes keypoints data over NNG
// Usage: dotnet-script keypoints_writer.csx <ipc_address> <output_file>
// Writes a single keypoints frame with test data

using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using nng;
using nng.Factories.Latest;

var address = Args.Count > 0 ? Args[0] : "ipc:///tmp/rocket-welder-keypoints-test";
var outputFile = Args.Count > 1 ? Args[1] : "/tmp/rocket-welder-test/csharp_keypoints_written.txt";

Console.WriteLine($"[C# KeyPoints Writer] Binding to {address}");

try
{
    var factory = new Factory();
    var socket = factory.PusherOpen().Unwrap();
    socket.Listen(address).Unwrap();

    Console.WriteLine("[C# KeyPoints Writer] Bound, waiting for connection...");
    Thread.Sleep(500);

    // Build keypoints frame manually (matching SDK format)
    using var buffer = new MemoryStream();

    // Frame type: 0x00 = Master frame
    buffer.WriteByte(0x00);

    // Frame ID (8 bytes, little-endian)
    var frameIdBytes = new byte[8];
    BinaryPrimitives.WriteUInt64LittleEndian(frameIdBytes, 42UL);
    buffer.Write(frameIdBytes, 0, 8);

    // Keypoint count (varint) - 3 keypoints
    WriteVarint(buffer, 3);

    // Keypoint 0: ID=0, X=100, Y=200, Confidence=9500 (0.95)
    WriteVarint(buffer, 0);  // keypoint ID
    var coordBytes = new byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(coordBytes, 100);
    buffer.Write(coordBytes, 0, 4);  // X
    BinaryPrimitives.WriteInt32LittleEndian(coordBytes, 200);
    buffer.Write(coordBytes, 0, 4);  // Y
    var confBytes = new byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(confBytes, 9500);
    buffer.Write(confBytes, 0, 2);  // Confidence

    // Keypoint 1: ID=1, X=150, Y=250, Confidence=9200 (0.92)
    WriteVarint(buffer, 1);
    BinaryPrimitives.WriteInt32LittleEndian(coordBytes, 150);
    buffer.Write(coordBytes, 0, 4);
    BinaryPrimitives.WriteInt32LittleEndian(coordBytes, 250);
    buffer.Write(coordBytes, 0, 4);
    BinaryPrimitives.WriteUInt16LittleEndian(confBytes, 9200);
    buffer.Write(confBytes, 0, 2);

    // Keypoint 2: ID=2, X=120, Y=180, Confidence=8800 (0.88)
    WriteVarint(buffer, 2);
    BinaryPrimitives.WriteInt32LittleEndian(coordBytes, 120);
    buffer.Write(coordBytes, 0, 4);
    BinaryPrimitives.WriteInt32LittleEndian(coordBytes, 180);
    buffer.Write(coordBytes, 0, 4);
    BinaryPrimitives.WriteUInt16LittleEndian(confBytes, 8800);
    buffer.Write(confBytes, 0, 2);

    var frameData = buffer.ToArray();
    Console.WriteLine($"[C# KeyPoints Writer] Sending {frameData.Length} bytes");

    socket.Send(frameData).Unwrap();

    // Write result
    Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
    File.WriteAllText(outputFile, $"written: frame_id=42, keypoints=3, bytes={frameData.Length}");

    Console.WriteLine("[C# KeyPoints Writer] Sent successfully!");
    Thread.Sleep(100);

    socket.Dispose();
}
catch (Exception ex)
{
    Console.WriteLine($"[C# KeyPoints Writer] Error: {ex.Message}");
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
