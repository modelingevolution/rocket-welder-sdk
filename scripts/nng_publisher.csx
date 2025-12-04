#!/usr/bin/env dotnet-script
#r "nuget: ModelingEvolution.Nng, 1.0.2"

// C# NNG Publisher - publishes frames to subscribers
// Usage: dotnet-script nng_publisher.csx <ipc_address> <message>

using System;
using System.Threading;
using nng;
using nng.Factories.Latest;

var address = Args.Count > 0 ? Args[0] : "ipc:///tmp/rocket-welder-pubsub-test";
var message = Args.Count > 1 ? Args[1] : "Hello from C# Publisher!";

Console.WriteLine($"[C# Publisher] Binding to {address}");

try
{
    var factory = new Factory();
    var socket = factory.PublisherOpen().Unwrap();
    socket.Listen(address).Unwrap();

    Console.WriteLine("[C# Publisher] Bound, waiting for subscribers...");
    Thread.Sleep(3000); // Give time for subscribers to connect (cross-platform tests need more time)

    var data = System.Text.Encoding.UTF8.GetBytes(message);
    Console.WriteLine($"[C# Publisher] Publishing {data.Length} bytes: {message}");

    socket.Send(data).Unwrap();

    Console.WriteLine("[C# Publisher] Published successfully!");
    Thread.Sleep(100); // Give time for message to be delivered

    socket.Dispose();
}
catch (Exception ex)
{
    Console.WriteLine($"[C# Publisher] Error: {ex.Message}");
    Environment.Exit(1);
}
