using System;
using System.Reflection;
using ZeroBuffer;
using ZeroBuffer.DuplexChannel;

class Program
{
    static void Main()
    {
        // Get all types from ZeroBuffer assembly
        var assembly = typeof(Reader).Assembly;
        Console.WriteLine($"Assembly: {assembly.FullName}");
        Console.WriteLine("\nException types in ZeroBuffer:");
        
        foreach (var type in assembly.GetTypes())
        {
            if (typeof(Exception).IsAssignableFrom(type) && !type.IsAbstract)
            {
                Console.WriteLine($"  - {type.FullName}");
            }
        }
        
        // Also check for public types that might be relevant
        Console.WriteLine("\nAll public types with Exception/Error in name:");
        foreach (var type in assembly.GetExportedTypes())
        {
            if (type.Name.Contains("Exception") || type.Name.Contains("Error"))
            {
                Console.WriteLine($"  - {type.FullName}");
            }
        }
    }
}