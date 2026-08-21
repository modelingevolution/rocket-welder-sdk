namespace RocketWelder.SDK.Devices.Motion.Delta;

/// <summary>
/// One serialised session to a drive, as the axis sees it.
///
/// <para>
/// An interface rather than a concrete class so the axis's own logic — the state machine, the
/// register write order, the homing latch sequence, the speed conversion — can be tested against a
/// register bank instead of a socket. Physics is <b>not</b> tested that way: anything about coast,
/// braking margin or the continuous→pulse handover is a bench measurement, and the logic tests
/// deliberately assert only what the adapter <i>writes</i> and <i>decides</i>.
/// </para>
/// </summary>
internal interface IModbusChannel : IDisposable
{
    /// <summary>The drive's endpoint, for messages and logging.</summary>
    string Host { get; }

    /// <summary>Whether the session is currently open.</summary>
    bool IsConnected { get; }

    /// <summary>Cheap reachability probe — no Modbus transaction.</summary>
    Task<bool> IsAvailableAsync(TimeSpan timeout, CancellationToken ct);

    /// <summary>Opens the session.</summary>
    Task ConnectAsync(CancellationToken ct);

    /// <summary>Closes the session without disposing it.</summary>
    void Disconnect();

    /// <summary>Reads holding registers.</summary>
    Task<ushort[]> ReadHoldingAsync(byte unit, ushort address, ushort count, string what,
        ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default);

    /// <summary>Writes one holding register.</summary>
    Task WriteRegisterAsync(byte unit, ushort address, ushort value, string what,
        ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default);

    /// <summary>Writes consecutive holding registers in one transaction.</summary>
    Task WriteRegistersAsync(byte unit, ushort address, ushort[] values, string what,
        ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default);

    /// <summary>Reads coils.</summary>
    Task<bool[]> ReadCoilsAsync(byte unit, ushort address, ushort count, string what,
        ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default);

    /// <summary>Writes one coil.</summary>
    Task WriteCoilAsync(byte unit, ushort address, bool value, string what,
        ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default);

    /// <summary>Reads discrete inputs.</summary>
    Task<bool[]> ReadDiscreteInputsAsync(byte unit, ushort address, ushort count, string what,
        ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default);
}

/// <summary>
/// The DWORD helpers, composed from the primitives so every <see cref="IModbusChannel"/> — real or
/// test — gets exactly one implementation of the C2000's word order.
/// </summary>
internal static class ModbusChannelExtensions
{
    /// <summary>
    /// Reads a signed 32-bit value stored as two consecutive registers, <b>low word first</b> — the
    /// layout the C2000 PLC uses for its DWORD devices.
    /// </summary>
    public static async Task<int> ReadDWordAsync(this IModbusChannel channel, byte unit, ushort address,
        string what, ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default)
    {
        var words = await channel.ReadHoldingAsync(unit, address, 2, what, priority, ct);
        return unchecked((int)(((uint)words[1] << 16) | words[0]));
    }

    /// <summary>Writes a signed 32-bit value as two registers, low word first.</summary>
    public static Task WriteDWordAsync(this IModbusChannel channel, byte unit, ushort address, int value,
        string what, ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default)
    {
        var raw = unchecked((uint)value);
        return channel.WriteRegistersAsync(unit, address,
            [(ushort)(raw & 0xFFFF), (ushort)(raw >> 16)], what, priority, ct);
    }
}
