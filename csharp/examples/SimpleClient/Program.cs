using System;
using RocketWelder.SDK;
using OpenCvSharp;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Starting RocketWelder client...");
        
        // Parse exit-after parameter
        int exitAfter = -1;  // -1 means run forever
        bool shouldExit = false;
        
        foreach (var arg in args)
        {
            if (arg.StartsWith("--exit-after="))
            {
                exitAfter = int.Parse(arg.Substring(13));
            }
        }
        
        // Create client from args/environment
        var client = RocketWelderClient.From(args);
        
        int frameCount = 0;
        
        // Set up frame processing
        client.OnFrame((Mat frame) =>
        {
            frameCount++;
            
            // Add overlay text
            Cv2.PutText(frame, "Processing", new Point(10, 30),
                       HersheyFonts.HersheySimplex, 1.0, new Scalar(0, 255, 0), 2);
            
            // Add frame counter overlay
            Cv2.PutText(frame, $"Frame: {frameCount}", new Point(10, 60),
                       HersheyFonts.HersheySimplex, 0.5, new Scalar(255, 255, 255), 1);
            
            Console.WriteLine($"Processed frame {frameCount} ({frame.Width}x{frame.Height})");
            
            // Check if we should exit
            if (exitAfter > 0 && frameCount >= exitAfter)
            {
                Console.WriteLine($"Reached {exitAfter} frames, exiting...");
                shouldExit = true;
            }
        });
        
        // Start processing
        if (exitAfter > 0)
        {
            Console.WriteLine($"Will exit after {exitAfter} frames");
        }
        client.Start();
        
        // Run until interrupted or frame limit reached
        if (exitAfter > 0)
        {
            while (!shouldExit && client.IsRunning)
            {
                System.Threading.Thread.Sleep(100);
            }
        }
        else
        {
            Console.WriteLine("Press any key to stop...");
            Console.ReadKey();
        }
        
        // Stop processing
        Console.WriteLine("Stopping client...");
        Console.WriteLine($"Total frames processed: {frameCount}");
        client.Stop();
        client.Dispose();
    }
}