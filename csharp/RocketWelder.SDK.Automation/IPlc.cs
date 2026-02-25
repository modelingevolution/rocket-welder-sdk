namespace RocketWelder.SDK.Automation;

/// <summary>
/// Protocol-agnostic interface for PLC communication.
/// Programs read/write named tags without knowing the underlying protocol.
/// Implementations: ModbusPlc (Modbus TCP via FluentModbus), etc.
/// </summary>
public interface IPlc : IDisposable
{
    // === Connection ===

    /// <summary>PLC endpoint address. Scheme encodes the protocol:
    /// <list type="bullet">
    /// <item>Modbus TCP: <c>modbus://192.168.1.10:502</c></item>
    /// </list></summary>
    Uri Address { get; set; }

    /// <summary>Whether the PLC is connected and ready for I/O.</summary>
    bool IsConnected { get; }

    /// <summary>Checks if the PLC is reachable on the network (TCP connect probe).</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>Connects to the PLC using <see cref="Address"/>.</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>Disconnects from the PLC.</summary>
    void Disconnect();

    /// <summary>Raised when connection is established.</summary>
    event EventHandler? Connected;

    /// <summary>Raised when connection is lost.</summary>
    event EventHandler? Disconnected;

    // === Tag I/O ===

    /// <summary>Reads the current value of a tag.</summary>
    /// <exception cref="UnknownTagException">Tag is not mapped.</exception>
    Task<T> ReadAsync<T>(PlcTag<T> tag, CancellationToken ct = default) where T : unmanaged;

    /// <summary>Writes a value to a tag.</summary>
    /// <exception cref="UnknownTagException">Tag is not mapped.</exception>
    Task WriteAsync<T>(PlcTag<T> tag, T value, CancellationToken ct = default) where T : unmanaged;

    /// <summary>Observes a tag value over time (poll + distinct-until-changed).</summary>
    /// <exception cref="UnknownTagException">Tag is not mapped.</exception>
    IObservable<T> Observe<T>(PlcTag<T> tag) where T : unmanaged;
}
