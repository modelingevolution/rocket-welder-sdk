#!/usr/bin/env dotnet-script
#r "nuget: ModelingEvolution.Nng, 1.0.2"

// C# NNG Pusher - sends frames to Python Puller
// Usage: dotnet-script nng_pusher.csx <ipc_address> <message>

using System;
using System.Threading;
using nng;
using nng.Factories.Latest;

var address = Args.Count > 0 ? Args[0] : "ipc:///tmp/rocket-welder-cross-platform-nng";
var message = Args.Count > 1 ? Args[1] : "Hello from C# NNG!";

Console.WriteLine($"[C# Pusher] Binding to {address}");

try
{
    var factory = new Factory();
    var socket = factory.PusherOpen().Unwrap();
    socket.Listen(address).Unwrap();

    Console.WriteLine("[C# Pusher] Bound, waiting for connection...");
    Thread.Sleep(500); // Give time for Python to connect

    var data = System.Text.Encoding.UTF8.GetBytes(message);
    Console.WriteLine($"[C# Pusher] Sending {data.Length} bytes: {message}");

    socket.Send(data).Unwrap();

    Console.WriteLine("[C# Pusher] Sent successfully!");
    Thread.Sleep(100); // Give time for message to be delivered

    socket.Dispose();
}
catch (Exception ex)
{
    Console.WriteLine($"[C# Pusher] Error: {ex.Message}");
    Environment.Exit(1);
}
