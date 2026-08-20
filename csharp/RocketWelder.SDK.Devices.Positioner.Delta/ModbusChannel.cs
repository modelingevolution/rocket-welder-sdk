using System.Net;
using System.Net.Sockets;
using FluentModbus;
using Microsoft.Extensions.Logging;

namespace RocketWelder.SDK.Devices.Positioner.Delta;

/// <summary>
/// One serialised Modbus TCP session to a drive.
///
/// <para>
/// All access is serialised: the drive's EtherNet/IP card handles a single request at a time, and
/// the control loop and status reads share this channel. Every operation retries once after
/// reconnecting, because that card drops the TCP connection without warning.
/// </para>
/// </summary>
internal sealed class ModbusChannel : IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ModbusTcpClient _client = new();
    private bool _disposed;

    public ModbusChannel(string host, int port, ILogger? logger)
    {
        _host = host;
        _port = port;
        _logger = logger;
    }

    public bool IsConnected => !_disposed && _client.IsConnected;

    public string Host => _host;

    /// <summary>Cheap TCP probe — opens and closes a socket, no Modbus transaction.</summary>
    public async Task<bool> IsAvailableAsync(TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            using var probe = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            await probe.ConnectAsync(_host, _port, cts.Token);
            return probe.Connected;
        }
        catch
        {
            return false;
        }
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            EnsureConnected();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Disconnect()
    {
        _gate.Wait();
        try
        {
            if (_client.IsConnected) _client.Disconnect();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Disconnecting {Host} threw; ignoring", _host);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureConnected()
    {
        if (_client.IsConnected) return;
        var endpoint = IPAddress.TryParse(_host, out var ip)
            ? new IPEndPoint(ip, _port)
            : new IPEndPoint(Dns.GetHostAddresses(_host)[0], _port);
        _client.Connect(endpoint, ModbusEndianness.BigEndian);
    }

    /// <summary>
    /// Runs one transaction under the channel lock, retrying once after a reconnect.
    /// </summary>
    /// <exception cref="PositionerException">Both attempts failed.</exception>
    public async Task<T> ExecuteAsync<T>(Func<ModbusTcpClient, T> operation, string what, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    EnsureConnected();
                    return operation(_client);
                }
                catch (Exception ex) when (attempt == 0)
                {
                    _logger?.LogDebug(ex, "{Host}: {What} failed, reconnecting and retrying", _host, what);
                    Reset();
                    await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
                }
                catch (Exception ex)
                {
                    throw new PositionerException(PositionerError.CommunicationFailed,
                        $"{_host}: {what} failed — {ex.Message}", ex);
                }
            }

            throw new PositionerException(PositionerError.CommunicationFailed,
                $"{_host}: {what} failed after retry");
        }
        finally
        {
            _gate.Release();
        }
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

    public Task<ushort[]> ReadHoldingAsync(byte unit, ushort address, ushort count, string what, CancellationToken ct)
        => ExecuteAsync(c => c.ReadHoldingRegisters<ushort>(unit, address, count).ToArray(), what, ct);

    public Task WriteRegisterAsync(byte unit, ushort address, ushort value, string what, CancellationToken ct)
        => ExecuteAsync<object?>(c => { c.WriteSingleRegister(unit, address, (short)value); return null; }, what, ct);

    public Task WriteRegistersAsync(byte unit, ushort address, ushort[] values, string what, CancellationToken ct)
        => ExecuteAsync<object?>(c =>
        {
            var signed = new short[values.Length];
            for (var i = 0; i < values.Length; i++) signed[i] = (short)values[i];
            c.WriteMultipleRegisters(unit, address, signed);
            return null;
        }, what, ct);

    public Task<bool[]> ReadCoilsAsync(byte unit, ushort address, ushort count, string what, CancellationToken ct)
        => ExecuteAsync(c => Unpack(c.ReadCoils(unit, address, count), count), what, ct);

    public Task WriteCoilAsync(byte unit, ushort address, bool value, string what, CancellationToken ct)
        => ExecuteAsync<object?>(c => { c.WriteSingleCoil(unit, address, value); return null; }, what, ct);

    public Task<bool[]> ReadDiscreteInputsAsync(byte unit, ushort address, ushort count, string what, CancellationToken ct)
        => ExecuteAsync(c => Unpack(c.ReadDiscreteInputs(unit, address, count), count), what, ct);

    /// <summary>
    /// Reads a signed 32-bit value stored as two consecutive registers, low word first — the layout
    /// the C2000 PLC uses for its DWORD devices.
    /// </summary>
    public async Task<int> ReadDWordAsync(byte unit, ushort address, string what, CancellationToken ct)
    {
        var words = await ReadHoldingAsync(unit, address, 2, what, ct);
        return unchecked((int)(((uint)words[1] << 16) | words[0]));
    }

    /// <summary>Writes a signed 32-bit value as two registers, low word first.</summary>
    public Task WriteDWordAsync(byte unit, ushort address, int value, string what, CancellationToken ct)
    {
        var raw = unchecked((uint)value);
        return WriteRegistersAsync(unit, address, [(ushort)(raw & 0xFFFF), (ushort)(raw >> 16)], what, ct);
    }

    private static bool[] Unpack(ReadOnlySpan<byte> packed, ushort count)
    {
        var bits = new bool[count];
        for (var i = 0; i < count; i++)
            bits[i] = (packed[i / 8] & (1 << (i % 8))) != 0;
        return bits;
    }

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
