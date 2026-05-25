using RocketWelder.SDK.Http.Cameras;
using RocketWelder.SDK.Http.Devices;
using RocketWelder.SDK.Http.DistanceSensors;
using RocketWelder.SDK.Http.Modbus;
using RocketWelder.SDK.Http.Pipelines;
using RocketWelder.SDK.Http.Programs;
using RocketWelder.SDK.Http.Robots;
using RocketWelder.SDK.Http.Skills;

namespace RocketWelder.SDK.Http;

/// <summary>
/// Strongly-typed entry point to the rocket-welder2 REST API.
/// Sub-API groups (<see cref="Devices"/>, <see cref="Pipelines"/>,
/// <see cref="Programs"/>, <see cref="Robots"/>, <see cref="Cameras"/>,
/// <see cref="DistanceSensors"/>, <see cref="Modbus"/>, <see cref="Skills"/>)
/// follow the URL grouping of the server.
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

    /// <summary>Operations under <c>/api/pipelines</c> + <c>/api/pipeline/{id}</c>.</summary>
    IPipelinesApi Pipelines { get; }

    /// <summary>Operations under <c>/api/programs</c> + <c>/api/repositories/{id}/compile</c>.</summary>
    IProgramsApi Programs { get; }

    /// <summary>Operations under <c>/api/robots/{name?}/...</c>.</summary>
    IRobotsApi Robots { get; }

    /// <summary>Operations under <c>/api/cameras/{name?}/...</c>.</summary>
    ICamerasApi Cameras { get; }

    /// <summary>Operations under <c>/api/distance-sensors/{name?}/...</c>.</summary>
    IDistanceSensorsApi DistanceSensors { get; }

    /// <summary>Operations under <c>/api/modbus/{device}/{tag}</c>.</summary>
    IModbusApi Modbus { get; }

    /// <summary>Operations under <c>/api/skills</c>.</summary>
    ISkillsApi Skills { get; }
}
