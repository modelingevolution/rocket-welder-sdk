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
        Pipelines = new PipelinesApi(http);
        Programs = new ProgramsApi(http);
        Robots = new RobotsApi(http);
        Cameras = new CamerasApi(http);
        DistanceSensors = new DistanceSensorsApi(http);
        Modbus = new ModbusApi(http);
        Skills = new SkillsApi(http);
    }

    public IDevicesApi Devices { get; }
    public IPipelinesApi Pipelines { get; }
    public IProgramsApi Programs { get; }
    public IRobotsApi Robots { get; }
    public ICamerasApi Cameras { get; }
    public IDistanceSensorsApi DistanceSensors { get; }
    public IModbusApi Modbus { get; }
    public ISkillsApi Skills { get; }
}
