using RocketWelder.SDK.Http.Devices;

namespace RocketWelder.SDK.Http;

/// <summary>
/// Default <see cref="IRocketWelderClient"/>. Holds the singleton
/// <see cref="HttpClient"/> (typically supplied by <c>IHttpClientFactory</c>)
/// and lazily exposes each sub-API.
/// </summary>
public sealed class RocketWelderClient : IRocketWelderClient
{
    public RocketWelderClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        Devices = new DevicesApi(http);
    }

    public IDevicesApi Devices { get; }
}
