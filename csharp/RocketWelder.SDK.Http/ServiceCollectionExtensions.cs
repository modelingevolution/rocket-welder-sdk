using Microsoft.Extensions.DependencyInjection;

namespace RocketWelder.SDK.Http;

/// <summary>
/// DI registration for <see cref="IRocketWelderClient"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IRocketWelderClient"/> backed by a pooled
    /// <see cref="HttpClient"/>. The default base address is
    /// <c>http://localhost:9001</c> so a host running rocket-welder2 in-process
    /// calls itself with zero configuration; override via <paramref name="configure"/>
    /// for remote scenarios (terminal MCP, Modellution IDE, external services).
    /// </summary>
    /// <example>
    /// <code>
    /// // In-process consumer (rocket-welder2 host):
    /// services.AddRocketWelderClient();
    ///
    /// // Remote consumer (MCP tool calling a welder over the network):
    /// services.AddRocketWelderClient(http =>
    /// {
    ///     http.BaseAddress = new Uri("http://welder.local:9001");
    ///     http.DefaultRequestHeaders.Authorization = ...; // when auth lands
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddRocketWelderClient(
        this IServiceCollection services,
        Action<HttpClient>? configure = null)
    {
        services.AddHttpClient<IRocketWelderClient, RocketWelderClient>(http =>
        {
            http.BaseAddress = new Uri("http://localhost:9001");
            configure?.Invoke(http);
        });
        return services;
    }
}
