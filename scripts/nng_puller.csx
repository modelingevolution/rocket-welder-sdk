#!/usr/bin/env dotnet-script
#r "nuget: ModelingEvolution.Nng, 1.0.2"

// C# NNG Puller - receives frames from Python Pusher
// Usage: dotnet-script nng_puller.csx <ipc_address> <output_file>

using System;
using System.IO;
using System.Threading;
using nng;
using nng.Factories.Latest;

var address = Args.Count > 0 ? Args[0] : "ipc:///tmp/rocket-welder-cross-platform-nng";
var outputFile = Args.Count > 1 ? Args[1] : "/tmp/rocket-welder-test/csharp_nng_received.txt";

Console.WriteLine($"[C# Puller] Connecting to {address}");

try
{
    var factory = new Factory();
    var socket = factory.PullerOpen().Unwrap();
    socket.Dial(address).Unwrap();

    Console.WriteLine("[C# Puller] Connected, waiting for frame...");

    // Set receive timeout
    socket.SetOpt(nng.Native.Defines.NNG_OPT_RECVTIMEO, 5000);

    var result = socket.RecvMsg();
    if (result.IsOk())
    {
        var msg = result.Unwrap();
        var data = msg.AsSpan().ToArray();
        var text = System.Text.Encoding.UTF8.GetString(data);

        Console.WriteLine($"[C# Puller] Received {data.Length} bytes: {text}");

        // Write result file
        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
        File.WriteAllText(outputFile, $"received: {data.Length} bytes, content: {text}");

        msg.Dispose();
        Console.WriteLine("[C# Puller] Success!");
    }
    else
    {
        Console.WriteLine($"[C# Puller] Receive failed or timed out");
        File.WriteAllText(outputFile, "error: receive failed");
    }

    socket.Dispose();
}
catch (Exception ex)
{
    Console.WriteLine($"[C# Puller] Error: {ex.Message}");
    File.WriteAllText(outputFile, $"error: {ex.Message}");
}
