#!/usr/bin/env dotnet-script
#r "nuget: ModelingEvolution.Nng, 1.0.2"

// C# NNG Multi-Frame Puller - receives multiple frames from Python Pusher
// Usage: dotnet-script nng_multi_puller.csx <ipc_address> <frame_count> <output_file>

using System;
using System.Collections.Generic;
using System.IO;
using nng;
using nng.Factories.Latest;

var address = Args.Count > 0 ? Args[0] : "ipc:///tmp/rocket-welder-multi-test";
var expectedFrameCount = Args.Count > 1 ? int.Parse(Args[1]) : 5;
var outputFile = Args.Count > 2 ? Args[2] : "/tmp/rocket-welder-test/csharp_multi_received.txt";

Console.WriteLine($"[C# Multi-Puller] Connecting to {address}, expecting {expectedFrameCount} frames");

try
{
    var factory = new Factory();
    var socket = factory.PullerOpen().Unwrap();
    socket.Dial(address).Unwrap();

    Console.WriteLine("[C# Multi-Puller] Connected, waiting for frames...");

    socket.SetOpt(nng.Native.Defines.NNG_OPT_RECVTIMEO, 5000);

    var receivedFrames = new List<string>();
    for (int i = 0; i < expectedFrameCount; i++)
    {
        var result = socket.RecvMsg();
        if (result.IsOk())
        {
            var msg = result.Unwrap();
            var data = msg.AsSpan().ToArray();
            var text = System.Text.Encoding.UTF8.GetString(data);
            receivedFrames.Add(text);
            Console.WriteLine($"[C# Multi-Puller] Received frame {i}: {text}");
            msg.Dispose();
        }
        else
        {
            Console.WriteLine($"[C# Multi-Puller] Frame {i} receive failed");
            break;
        }
    }

    // Write result
    Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
    File.WriteAllText(outputFile, $"received: count={receivedFrames.Count}, frames=[{string.Join("; ", receivedFrames)}]");

    Console.WriteLine($"[C# Multi-Puller] Received {receivedFrames.Count} frames successfully!");

    socket.Dispose();
}
catch (Exception ex)
{
    Console.WriteLine($"[C# Multi-Puller] Error: {ex.Message}");
    File.WriteAllText(outputFile, $"error: {ex.Message}");
}
