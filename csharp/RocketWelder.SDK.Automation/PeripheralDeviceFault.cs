using MicroPlumberd;
using RocketWelder.SDK.Abstractions;

namespace RocketWelder.SDK.Automation;

/// <summary>Fault payload returned by the peripheral-device command handler on validation failure.</summary>
public record PeripheralDeviceFault
{
    public required DeviceId DeviceId { get; init; }
    public required string Reason { get; init; }
}

/// <summary>Thrown by the command handler when a peripheral device command cannot be fulfilled.</summary>
public class PeripheralDeviceFaultException : FaultException<PeripheralDeviceFault>
{
    public PeripheralDeviceFaultException(DeviceId peripheralDeviceId, string reason)
        : base(reason, new PeripheralDeviceFault { DeviceId = peripheralDeviceId, Reason = reason }, 400)
    {
    }
}
