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
    /// A connection failure or a timeout throws <see cref="StationUnreachableException"/> naming the base address
    /// (<see cref="StationUnreachableHandler"/>).
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
        // epic-128 feature-001 design.md §4 (ADR-13): one place decides "station unreachable" for every caller. The
        // handler owns the configured timeout, so it can tell a timeout from the caller's own cancellation. A second call
        // (a host may register the client before a tool pack does) shares the one handler and its settings, so the
        // handler is never stacked; the last configuration wins, as it does for the client itself.
        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(StationClientSettings))?.ImplementationInstance
            as StationClientSettings;
        var station = existing ?? new StationClientSettings();
        var builder = services.AddHttpClient<IRocketWelderClient, RocketWelderClient>(http =>
            {
                http.BaseAddress = new Uri("http://localhost:9001");
                // Bounded default so a hung server doesn't pin a tool invocation
                // indefinitely (.NET's HttpClient default is 100s). Override via the
                // caller-supplied configure delegate.
                http.Timeout = TimeSpan.FromSeconds(30);
                configure?.Invoke(http);
                station.TakeOver(http);
            });
        if (existing is null)
        {
            services.AddSingleton(station);
            builder.AddHttpMessageHandler(() => new StationUnreachableHandler(station));
        }
        return services;
    }
}
