using RocketWelder.SDK.Internal;

namespace RocketWelder.SDK;

/// <summary>
/// Factory for creating RocketWelderClient instances.
/// </summary>
public static class RocketWelderClientFactory
{
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
