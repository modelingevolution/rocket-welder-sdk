#!/usr/bin/env dotnet-script
#r "nuget: ModelingEvolution.Nng, 1.0.2"

// C# Segmentation Reader - reads segmentation data over NNG
// Usage: dotnet-script segmentation_reader.csx <ipc_address> <output_file>
// Reads a single segmentation frame and verifies its content

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using nng;
using nng.Factories.Latest;

var address = Args.Count > 0 ? Args[0] : "ipc:///tmp/rocket-welder-segmentation-test";
var outputFile = Args.Count > 1 ? Args[1] : "/tmp/rocket-welder-test/csharp_segmentation_received.txt";

Console.WriteLine($"[C# Segmentation Reader] Connecting to {address}");

try
{
    var factory = new Factory();
    var socket = factory.PullerOpen().Unwrap();
    socket.Dial(address).Unwrap();

    Console.WriteLine("[C# Segmentation Reader] Connected, waiting for frame...");

    socket.SetOpt(nng.Native.Defines.NNG_OPT_RECVTIMEO, 5000);

    var result = socket.RecvMsg();
    if (result.IsOk())
    {
        var msg = result.Unwrap();
        var data = msg.AsSpan().ToArray();

        Console.WriteLine($"[C# Segmentation Reader] Received {data.Length} bytes");

        // Parse segmentation frame
        using var stream = new MemoryStream(data);

        // Read frame ID (8 bytes, little-endian)
        var frameIdBytes = new byte[8];
        stream.Read(frameIdBytes, 0, 8);
        ulong frameId = BinaryPrimitives.ReadUInt64LittleEndian(frameIdBytes);
        Console.WriteLine($"[C# Segmentation Reader] Frame ID: {frameId}");

        // Read width and height (varints)
        uint width = ReadVarint(stream);
        uint height = ReadVarint(stream);
        Console.WriteLine($"[C# Segmentation Reader] Dimensions: {width}x{height}");

        // Read instances
        var instances = new List<string>();
        int instanceIndex = 0;
        while (stream.Position < stream.Length)
        {
            int classIdByte = stream.ReadByte();
            if (classIdByte == -1) break;

            int instanceIdByte = stream.ReadByte();
            if (instanceIdByte == -1) break;

            byte classId = (byte)classIdByte;
            byte instanceId = (byte)instanceIdByte;

            uint pointCount = ReadVarint(stream);
            Console.WriteLine($"[C# Segmentation Reader]   Instance {instanceIndex}: class={classId}, instance={instanceId}, points={pointCount}");

            // Read points with delta decoding
            var points = new List<Point>();
            if (pointCount > 0)
            {
                // First point (absolute, zigzag encoded)
                int x = ZigZagDecode(ReadVarint(stream));
                int y = ZigZagDecode(ReadVarint(stream));
                points.Add(new Point(x, y));

                // Remaining points (delta encoded)
                for (int i = 1; i < pointCount; i++)
                {
                    int deltaX = ZigZagDecode(ReadVarint(stream));
                    int deltaY = ZigZagDecode(ReadVarint(stream));
                    x += deltaX;
                    y += deltaY;
                    points.Add(new Point(x, y));
                }
            }

            var pointsStr = string.Join(",", points.Select(p => $"({p.X},{p.Y})"));
            instances.Add($"class={classId},instance={instanceId},points=[{pointsStr}]");
            instanceIndex++;
        }

        // Write result
        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
        File.WriteAllText(outputFile, $"received: frame_id={frameId}, width={width}, height={height}, instances=[{string.Join("; ", instances)}]");

        msg.Dispose();
        Console.WriteLine("[C# Segmentation Reader] Success!");
    }
    else
    {
        Console.WriteLine("[C# Segmentation Reader] Receive failed or timed out");
        File.WriteAllText(outputFile, "error: receive failed");
    }

    socket.Dispose();
}
catch (Exception ex)
{
    Console.WriteLine($"[C# Segmentation Reader] Error: {ex.Message}");
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

int ZigZagDecode(uint value)
{
    return (int)(value >> 1) ^ -(int)(value & 1);
}
