using System.Net;
using System.Net.Sockets;
using FluentModbus;
using Microsoft.Extensions.Logging;

namespace RocketWelder.SDK.Devices.Motion.Delta;

/// <summary>
/// One serialised Modbus TCP session to a drive.
///
/// <para>
/// All access is serialised: the drive's EtherNet/IP card handles a single request at a time, and
/// the move loop, the status poll and the FR-11 heartbeat share this channel. Serialisation goes
/// through <see cref="PriorityGate"/> rather than a plain lock, so a stop preempts queued move
/// traffic (NFR-5 / AC-23) and the heartbeat's deferral is bounded (FR-11 / AC-24).
/// </para>
///
/// <para>
/// Every operation retries once after reconnecting, because that card drops the TCP connection
/// without warning.
/// </para>
/// </summary>
internal sealed class ModbusChannel : IModbusChannel
{
    private readonly int _port;
    private readonly ILogger? _logger;
    private readonly PriorityGate _gate;
    private ModbusTcpClient _client = new();
    private bool _disposed;

    public ModbusChannel(string host, int port, ILogger? logger, PriorityGate? gate = null)
    {
        Host = host;
        _port = port;
        _logger = logger;
        _gate = gate ?? new PriorityGate();
    }

    /// <inheritdoc/>
    public string Host { get; }

    /// <inheritdoc/>
    public bool IsConnected => !_disposed && _client.IsConnected;

    /// <inheritdoc/>
    public async Task<bool> IsAvailableAsync(TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            using var probe = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            await probe.ConnectAsync(Host, _port, cts.Token);
            return probe.Connected;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken ct)
    {
        using var _ = await _gate.AcquireAsync(ChannelPriority.Move, ct);
        EnsureConnected();
    }

    /// <inheritdoc/>
    public void Disconnect()
    {
        // Stop lane: disconnect is part of the shutdown path that must not queue behind a move.
        using var _ = _gate.AcquireAsync(ChannelPriority.Stop, CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        try
        {
            if (_client.IsConnected) _client.Disconnect();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Disconnecting {Host} threw; ignoring", Host);
        }
    }

    private void EnsureConnected()
    {
        if (_client.IsConnected) return;
        var endpoint = IPAddress.TryParse(Host, out var ip)
            ? new IPEndPoint(ip, _port)
            : new IPEndPoint(Dns.GetHostAddresses(Host)[0], _port);
        _client.Connect(endpoint, ModbusEndianness.BigEndian);
    }

    /// <summary>
    /// Runs one transaction in its lane, retrying once after a reconnect.
    /// </summary>
    /// <exception cref="MotionException">Both attempts failed
    /// (<see cref="MotionError.CommunicationLost"/>).</exception>
    private async Task<T> ExecuteAsync<T>(Func<ModbusTcpClient, T> operation, string what,
        ChannelPriority priority, CancellationToken ct)
    {
        // A disposed channel must not quietly reopen the socket. EnsureConnected would otherwise
        // reconnect a session the owner has deliberately torn down — which would also make a killed
        // commander look alive again for one transaction.
        if (_disposed)
            throw new MotionException(MotionError.CommunicationLost,
                $"{Host}: {what} attempted on a disposed channel");

        using var _ = await _gate.AcquireAsync(priority, ct);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                EnsureConnected();
                return operation(_client);
            }
            catch (Exception ex) when (attempt == 0)
            {
                _logger?.LogDebug(ex, "{Host}: {What} failed, reconnecting and retrying", Host, what);
                Reset();
                await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
            }
            catch (Exception ex)
            {
                throw new MotionException(MotionError.CommunicationLost,
                    $"{Host}: {what} failed — {ex.Message}");
            }
        }

        throw new MotionException(MotionError.CommunicationLost, $"{Host}: {what} failed after retry");
    }

    private void Reset()
    {
        try
        {
            if (_client.IsConnected) _client.Disconnect();
        }
        catch
        {
            // The point of resetting is that the old client is untrustworthy.
        }

        _client = new ModbusTcpClient();
    }

    // ── typed helpers ─────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<ushort[]> ReadHoldingAsync(byte unit, ushort address, ushort count, string what,
        ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default)
        => ExecuteAsync(c => c.ReadHoldingRegisters<ushort>(unit, address, count).ToArray(), what, priority, ct);

    /// <inheritdoc/>
    public Task WriteRegisterAsync(byte unit, ushort address, ushort value, string what,
        ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default)
        => ExecuteAsync<object?>(c => { c.WriteSingleRegister(unit, address, (short)value); return null; },
            what, priority, ct);

    /// <inheritdoc/>
    public Task WriteRegistersAsync(byte unit, ushort address, ushort[] values, string what,
        ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default)
        => ExecuteAsync<object?>(c =>
        {
            var signed = new short[values.Length];
            for (var i = 0; i < values.Length; i++) signed[i] = (short)values[i];
            c.WriteMultipleRegisters(unit, address, signed);
            return null;
        }, what, priority, ct);

    /// <inheritdoc/>
    public Task<bool[]> ReadCoilsAsync(byte unit, ushort address, ushort count, string what,
        ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default)
        => ExecuteAsync(c => Unpack(c.ReadCoils(unit, address, count), count), what, priority, ct);

    /// <inheritdoc/>
    public Task WriteCoilAsync(byte unit, ushort address, bool value, string what,
        ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default)
        => ExecuteAsync<object?>(c => { c.WriteSingleCoil(unit, address, value); return null; },
            what, priority, ct);

    /// <inheritdoc/>
    public Task<bool[]> ReadDiscreteInputsAsync(byte unit, ushort address, ushort count, string what,
        ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default)
        => ExecuteAsync(c => Unpack(c.ReadDiscreteInputs(unit, address, count), count), what, priority, ct);

    private static bool[] Unpack(ReadOnlySpan<byte> packed, ushort count)
    {
        var bits = new bool[count];
        for (var i = 0; i < count; i++)
            bits[i] = (packed[i / 8] & (1 << (i % 8))) != 0;
        return bits;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_client.IsConnected) _client.Disconnect();
        }
        catch
        {
            // Disposal must not throw.
        }

        _gate.Dispose();
    }
}
