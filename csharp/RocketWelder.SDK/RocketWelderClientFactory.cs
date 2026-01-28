using System;
using RocketWelder.SDK.Internal;

namespace RocketWelder.SDK;

/// <summary>
/// Factory for creating RocketWelderClient instances.
/// </summary>
public static class RocketWelderClientFactory
{
    /// <summary>
    /// Path to the ML model file mounted in the container.
    /// Reads from ML_MODEL_PATH environment variable.
    /// Convenience property for use before creating a client instance.
    /// </summary>
    /// <example>
    /// var modelPath = RocketWelderClientFactory.MlModelPath
    ///     ?? throw new InvalidOperationException("No ML model configured");
    /// var model = LoadModel(modelPath);
    /// </example>
    public static string? MlModelPath => Environment.GetEnvironmentVariable("ML_MODEL_PATH");

    /// <summary>
    /// Version of the ML model.
    /// Reads from ML_MODEL_VERSION environment variable.
    /// Returns null if not set or not a valid number.
    /// </summary>
    public static long? MlModelVersion
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("ML_MODEL_VERSION");
            return long.TryParse(value, out var version) ? version : null;
        }
    }

    /// <summary>
    /// Creates a client configured from environment variables.
    /// </summary>
    public static IRocketWelderClient FromEnvironment()
    {
        var options = RocketWelderClientOptions.FromEnvironment();
        return new RocketWelderClientImpl(options);
    }

    /// <summary>
    /// Creates a client with explicit configuration.
    /// </summary>
    public static IRocketWelderClient Create(RocketWelderClientOptions options)
    {
        return new RocketWelderClientImpl(options);
    }

    /// <summary>
    /// Creates a client with default options.
    /// </summary>
    public static IRocketWelderClient Create()
    {
        return new RocketWelderClientImpl(new RocketWelderClientOptions());
    }
}
