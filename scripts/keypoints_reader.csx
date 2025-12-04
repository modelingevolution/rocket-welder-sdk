#!/usr/bin/env dotnet-script
#r "nuget: ModelingEvolution.Nng, 1.0.2"

// C# KeyPoints Reader - reads keypoints data over NNG
// Usage: dotnet-script keypoints_reader.csx <ipc_address> <output_file>
// Reads a single keypoints frame and verifies its content

using System;
using System.Buffers.Binary;
using System.IO;
using nng;
using nng.Factories.Latest;

var address = Args.Count > 0 ? Args[0] : "ipc:///tmp/rocket-welder-keypoints-test";
var outputFile = Args.Count > 1 ? Args[1] : "/tmp/rocket-welder-test/csharp_keypoints_received.txt";

Console.WriteLine($"[C# KeyPoints Reader] Connecting to {address}");

try
{
    var factory = new Factory();
    var socket = factory.PullerOpen().Unwrap();
    socket.Dial(address).Unwrap();

    Console.WriteLine("[C# KeyPoints Reader] Connected, waiting for frame...");

    socket.SetOpt(nng.Native.Defines.NNG_OPT_RECVTIMEO, 5000);

    var result = socket.RecvMsg();
    if (result.IsOk())
    {
        var msg = result.Unwrap();
        var data = msg.AsSpan().ToArray();

        Console.WriteLine($"[C# KeyPoints Reader] Received {data.Length} bytes");

        // Parse keypoints frame
        using var stream = new MemoryStream(data);

        // Read frame type
        int frameType = stream.ReadByte();
        bool isDelta = frameType == 0x01;
        Console.WriteLine($"[C# KeyPoints Reader] Frame type: {(isDelta ? "Delta" : "Master")}");

        // Read frame ID
        var frameIdBytes = new byte[8];
        stream.Read(frameIdBytes, 0, 8);
        ulong frameId = BinaryPrimitives.ReadUInt64LittleEndian(frameIdBytes);
        Console.WriteLine($"[C# KeyPoints Reader] Frame ID: {frameId}");

        // Read keypoint count
        uint keypointCount = ReadVarint(stream);
        Console.WriteLine($"[C# KeyPoints Reader] Keypoint count: {keypointCount}");

        // Read keypoints
        var keypoints = new List<string>();
        for (int i = 0; i < keypointCount; i++)
        {
            int kpId = (int)ReadVarint(stream);

            var coordBytes = new byte[4];
            stream.Read(coordBytes, 0, 4);
            int x = BinaryPrimitives.ReadInt32LittleEndian(coordBytes);
            stream.Read(coordBytes, 0, 4);
            int y = BinaryPrimitives.ReadInt32LittleEndian(coordBytes);

            var confBytes = new byte[2];
            stream.Read(confBytes, 0, 2);
            ushort confRaw = BinaryPrimitives.ReadUInt16LittleEndian(confBytes);
            float confidence = confRaw / 10000f;

            keypoints.Add($"id={kpId},x={x},y={y},conf={confidence:F2}");
            Console.WriteLine($"[C# KeyPoints Reader]   KP{kpId}: ({x}, {y}) conf={confidence:F2}");
        }

        // Write result
        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
        File.WriteAllText(outputFile, $"received: frame_id={frameId}, keypoints=[{string.Join("; ", keypoints)}]");

        msg.Dispose();
        Console.WriteLine("[C# KeyPoints Reader] Success!");
    }
    else
    {
        Console.WriteLine("[C# KeyPoints Reader] Receive failed or timed out");
        File.WriteAllText(outputFile, "error: receive failed");
    }

    socket.Dispose();
}
catch (Exception ex)
{
    Console.WriteLine($"[C# KeyPoints Reader] Error: {ex.Message}");
    File.WriteAllText(outputFile, $"error: {ex.Message}");
}

uint ReadVarint(System.IO.Stream stream)
{
    uint result = 0;
    int shift = 0;
    byte b;
    do
    {
        int read = stream.ReadByte();
        if (read == -1) throw new EndOfStreamException();
        b = (byte)read;
        result |= (uint)(b & 0x7F) << shift;
        shift += 7;
    } while ((b & 0x80) != 0);
    return result;
}
