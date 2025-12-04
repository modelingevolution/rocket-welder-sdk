#!/usr/bin/env dotnet-script
#r "nuget: ModelingEvolution.Nng, 1.0.2"

// C# NNG Multi-Frame Pusher - sends multiple frames to Python Puller
// Usage: dotnet-script nng_multi_pusher.csx <ipc_address> <frame_count>

using System;
using System.Threading;
using nng;
using nng.Factories.Latest;

var address = Args.Count > 0 ? Args[0] : "ipc:///tmp/rocket-welder-multi-test";
var frameCount = Args.Count > 1 ? int.Parse(Args[1]) : 5;

Console.WriteLine($"[C# Multi-Pusher] Binding to {address}, sending {frameCount} frames");

try
{
    var factory = new Factory();
    var socket = factory.PusherOpen().Unwrap();
    socket.Listen(address).Unwrap();

    Console.WriteLine("[C# Multi-Pusher] Bound, waiting for connection...");
    Thread.Sleep(500);

    for (int i = 0; i < frameCount; i++)
    {
        var message = $"Frame {i} from C#";
        var data = System.Text.Encoding.UTF8.GetBytes(message);
        socket.Send(data).Unwrap();
        Console.WriteLine($"[C# Multi-Pusher] Sent frame {i}: {message}");
        Thread.Sleep(50); // Small delay between frames
    }

    Console.WriteLine($"[C# Multi-Pusher] Sent {frameCount} frames successfully!");
    Thread.Sleep(100);

    socket.Dispose();
}
catch (Exception ex)
{
    Console.WriteLine($"[C# Multi-Pusher] Error: {ex.Message}");
    Environment.Exit(1);
}
