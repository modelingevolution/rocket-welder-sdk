#!/usr/bin/env dotnet-script
#r "nuget: ModelingEvolution.Nng, 1.0.2"

// C# NNG Subscriber - receives frames from publisher
// Usage: dotnet-script nng_subscriber.csx <ipc_address> <output_file>

using System;
using System.IO;
using nng;
using nng.Factories.Latest;

var address = Args.Count > 0 ? Args[0] : "ipc:///tmp/rocket-welder-pubsub-test";
var outputFile = Args.Count > 1 ? Args[1] : "/tmp/rocket-welder-test/csharp_subscriber_received.txt";

Console.WriteLine($"[C# Subscriber] Connecting to {address}");

try
{
    var factory = new Factory();
    var socket = factory.SubscriberOpen().Unwrap();

    // Subscribe to all topics (empty topic = all messages)
    socket.SetOpt(nng.Native.Defines.NNG_OPT_SUB_SUBSCRIBE, new byte[0]);

    socket.Dial(address).Unwrap();

    Console.WriteLine("[C# Subscriber] Connected, waiting for message...");

    // Set receive timeout
    socket.SetOpt(nng.Native.Defines.NNG_OPT_RECVTIMEO, 5000);

    var result = socket.RecvMsg();
    if (result.IsOk())
    {
        var msg = result.Unwrap();
        var data = msg.AsSpan().ToArray();
        var text = System.Text.Encoding.UTF8.GetString(data);

        Console.WriteLine($"[C# Subscriber] Received {data.Length} bytes: {text}");

        // Write result to file
        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
        File.WriteAllText(outputFile, $"received: {text}");

        msg.Dispose();
        Console.WriteLine("[C# Subscriber] Success!");
    }
    else
    {
        Console.WriteLine("[C# Subscriber] Receive failed or timed out");
        File.WriteAllText(outputFile, "error: receive failed");
    }

    socket.Dispose();
}
catch (Exception ex)
{
    Console.WriteLine($"[C# Subscriber] Error: {ex.Message}");
    File.WriteAllText(outputFile, $"error: {ex.Message}");
    Environment.Exit(1);
}
