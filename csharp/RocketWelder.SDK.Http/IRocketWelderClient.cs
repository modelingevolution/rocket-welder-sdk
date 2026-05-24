using RocketWelder.SDK.Http.Devices;

namespace RocketWelder.SDK.Http;

/// <summary>
/// Strongly-typed entry point to the rocket-welder2 REST API.
/// Sub-API groups (<see cref="Devices"/>, future Pipelines / Programs / ...) follow
/// the URL grouping of the server: <c>/api/devices</c>, <c>/api/pipelines</c>, etc.
/// </summary>
/// <remarks>
/// <para>
/// One client, one wire shape — usable from the welder host itself (loopback),
/// from a terminal MCP tool, from the Modellution IDE, or any other external
/// .NET caller. Consumers should NOT new this up directly; register via
/// <c>services.AddRocketWelderClient(...)</c> and resolve through DI so the
/// underlying <see cref="HttpClient"/> is pooled and configured uniformly.
/// </para>
/// </remarks>
public interface IRocketWelderClient
{
    /// <summary>Operations under <c>/api/devices</c>.</summary>
    IDevicesApi Devices { get; }
}
