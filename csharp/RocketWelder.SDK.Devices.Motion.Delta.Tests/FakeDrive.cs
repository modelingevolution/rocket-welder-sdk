namespace RocketWelder.SDK.Devices.Motion.Delta.Tests;

/// <summary>
/// A register bank behind the <see cref="IModbusChannel"/> seam, and <b>deliberately nothing more</b>.
///
/// <para>
/// It is not a second simulator. It holds registers, coils and discrete inputs, records every
/// transaction in order, and applies exactly one wiring rule — the drive's output-frequency status
/// word mirrors the commanded frequency while the motion coil is on. <b>The position never moves.</b>
/// </para>
///
/// <para>
/// That boundary is the point: everything about how far the axis coasts, where it should brake and
/// when the continuous approach hands over to micro-pulses is bench-measured, and asserting it here
/// would be asserting against a model of a model. Tests using this fake assert what the adapter
/// <i>writes</i> and <i>decides</i> — register values, their order, the state machine, the speed
/// conversion, the rejections. Anything that has to move goes to the live simulator instead.
/// </para>
///
/// <para>
/// A test that needs the machine to react — the home latch firing, a limit switch tripping — scripts
/// that itself through <see cref="React"/> and <see cref="ShapeInputs"/>, so the reaction is visible
/// in the test rather than hidden in this class.
/// </para>
/// </summary>
internal sealed class FakeDrive : IModbusChannel
{
    private readonly Lock _sync = new();
    private readonly Dictionary<(byte Unit, ushort Address), ushort> _holding = [];
    private readonly Dictionary<(byte Unit, ushort Address), bool> _coils = [];
    private readonly List<Op> _ops = [];

    public FakeDrive(string host = "fake-drive")
    {
        Host = host;

        // Discrete inputs are normally CLOSED: 1 means "not tripped" and "not on the cam".
        Inputs = Enumerable.Repeat(true, DeltaRegisters.InputCount).ToArray();

        // Seed the drive parameters DIFFERENT from what the adapter enforces, exactly as the
        // simulator does, so the startup write path is exercised rather than short-circuited.
        foreach (var (address, value, _) in DeltaRegisters.RequiredSetup)
            _holding[(DeltaRegisters.DriveUnit, address)] = (ushort)(value + 1);
    }

    public string Host { get; }

    public bool IsConnected { get; private set; }

    /// <summary>The ten X inputs. 1 = not tripped / not on the cam (normally closed).</summary>
    public bool[] Inputs { get; }

    /// <summary>Invoked after every write, so a test can play the ladder's part.</summary>
    public Action<FakeDrive, Op>? React { get; set; }

    /// <summary>Invoked on every discrete-input read, so a test can script an input changing.</summary>
    public Action<FakeDrive>? ShapeInputs { get; set; }

    /// <summary>Every transaction, in the order it reached the wire.</summary>
    public IReadOnlyList<Op> Ops
    {
        get { lock (_sync) return _ops.ToArray(); }
    }

    public ushort ReadHolding(byte unit, ushort address)
    {
        lock (_sync) return _holding.GetValueOrDefault((unit, address));
    }

    public void WriteHolding(byte unit, ushort address, ushort value)
    {
        lock (_sync) _holding[(unit, address)] = value;
    }

    public bool ReadCoil(byte unit, ushort address)
    {
        lock (_sync) return _coils.GetValueOrDefault((unit, address));
    }

    /// <summary>The raw encoder count the position DWORD reports (D1051/D1052, low word first).</summary>
    public int PositionCounts
    {
        get
        {
            lock (_sync)
            {
                var lo = _holding.GetValueOrDefault((DeltaRegisters.PlcUnit, DeltaRegisters.D1051_Position));
                var hi = _holding.GetValueOrDefault((DeltaRegisters.PlcUnit, (ushort)(DeltaRegisters.D1051_Position + 1)));
                return unchecked((int)(((uint)hi << 16) | lo));
            }
        }
        set
        {
            var raw = unchecked((uint)value);
            lock (_sync)
            {
                _holding[(DeltaRegisters.PlcUnit, DeltaRegisters.D1051_Position)] = (ushort)(raw & 0xFFFF);
                _holding[(DeltaRegisters.PlcUnit, (ushort)(DeltaRegisters.D1051_Position + 1))] = (ushort)(raw >> 16);
            }
        }
    }

    /// <summary>The home-latch DWORD (D120/D121), as the ladder would leave it.</summary>
    public int HomeLatch
    {
        get
        {
            lock (_sync)
            {
                var lo = _holding.GetValueOrDefault((DeltaRegisters.PlcUnit, DeltaRegisters.D120_HomeLatch));
                var hi = _holding.GetValueOrDefault((DeltaRegisters.PlcUnit, (ushort)(DeltaRegisters.D120_HomeLatch + 1)));
                return unchecked((int)(((uint)hi << 16) | lo));
            }
        }
        set
        {
            var raw = unchecked((uint)value);
            lock (_sync)
            {
                _holding[(DeltaRegisters.PlcUnit, DeltaRegisters.D120_HomeLatch)] = (ushort)(raw & 0xFFFF);
                _holding[(DeltaRegisters.PlcUnit, (ushort)(DeltaRegisters.D120_HomeLatch + 1))] = (ushort)(raw >> 16);
            }
        }
    }

    /// <summary>The ladder's <c>DSUB</c> result DWORD (D122/D123).</summary>
    public int LatchDelta
    {
        get
        {
            lock (_sync)
            {
                var lo = _holding.GetValueOrDefault((DeltaRegisters.PlcUnit, DeltaRegisters.D122_LatchDelta));
                var hi = _holding.GetValueOrDefault((DeltaRegisters.PlcUnit, (ushort)(DeltaRegisters.D122_LatchDelta + 1)));
                return unchecked((int)(((uint)hi << 16) | lo));
            }
        }
        set
        {
            var raw = unchecked((uint)value);
            lock (_sync)
            {
                _holding[(DeltaRegisters.PlcUnit, DeltaRegisters.D122_LatchDelta)] = (ushort)(raw & 0xFFFF);
                _holding[(DeltaRegisters.PlcUnit, (ushort)(DeltaRegisters.D122_LatchDelta + 1))] = (ushort)(raw >> 16);
            }
        }
    }

    /// <summary>Writes to the transactions the adapter has issued so far, in order.</summary>
    public IEnumerable<Op> Writes => Ops.Where(o => o.IsWrite);

    public Task<bool> IsAvailableAsync(TimeSpan timeout, CancellationToken ct) => Task.FromResult(true);

    public Task ConnectAsync(CancellationToken ct)
    {
        IsConnected = true;
        return Task.CompletedTask;
    }

    public void Disconnect() => IsConnected = false;

    public Task<ushort[]> ReadHoldingAsync(byte unit, ushort address, ushort count, string what,
        ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            Record(new Op("read-holding", unit, address, what, priority, null));
            var values = new ushort[count];
            for (ushort i = 0; i < count; i++)
                values[i] = _holding.GetValueOrDefault((unit, (ushort)(address + i)));
            return Task.FromResult(values);
        }
    }

    public Task WriteRegisterAsync(byte unit, ushort address, ushort value, string what,
        ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default)
        => WriteRegistersAsync(unit, address, [value], what, priority, ct);

    public Task WriteRegistersAsync(byte unit, ushort address, ushort[] values, string what,
        ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Op op;
        lock (_sync)
        {
            for (ushort i = 0; i < values.Length; i++)
                _holding[(unit, (ushort)(address + i))] = values[i];
            op = new Op("write-holding", unit, address, what, priority, values);
            Record(op);
            MirrorOutputFrequency();
        }

        React?.Invoke(this, op);
        return Task.CompletedTask;
    }

    public Task<bool[]> ReadCoilsAsync(byte unit, ushort address, ushort count, string what,
        ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            Record(new Op("read-coils", unit, address, what, priority, null));
            var values = new bool[count];
            for (ushort i = 0; i < count; i++)
                values[i] = _coils.GetValueOrDefault((unit, (ushort)(address + i)));
            return Task.FromResult(values);
        }
    }

    public Task WriteCoilAsync(byte unit, ushort address, bool value, string what,
        ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Op op;
        lock (_sync)
        {
            _coils[(unit, address)] = value;
            op = new Op("write-coil", unit, address, what, priority, [value ? (ushort)1 : (ushort)0]);
            Record(op);
            MirrorOutputFrequency();
        }

        React?.Invoke(this, op);
        return Task.CompletedTask;
    }

    public Task<bool[]> ReadDiscreteInputsAsync(byte unit, ushort address, ushort count, string what,
        ChannelPriority priority = ChannelPriority.Move, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ShapeInputs?.Invoke(this);
        lock (_sync)
        {
            Record(new Op("read-inputs", unit, address, what, priority, null));
            var values = new bool[count];
            for (ushort i = 0; i < count; i++)
            {
                var index = address - DeltaRegisters.X0_Inputs + i;
                values[i] = index >= 0 && index < Inputs.Length && Inputs[index];
            }

            return Task.FromResult(values);
        }
    }

    /// <summary>
    /// The one wiring rule this fake applies: the drive's output-frequency status word follows the
    /// commanded frequency while <c>M4</c> is on. Not physics — no ramp, no coast, no motion — just
    /// the register the adapter reads back to decide the drive has stopped.
    /// </summary>
    private void MirrorOutputFrequency()
    {
        var moving = _coils.GetValueOrDefault((DeltaRegisters.PlcUnit, DeltaRegisters.M4_Move));
        var commanded = _holding.GetValueOrDefault((DeltaRegisters.PlcUnit, DeltaRegisters.D110_Frequency));
        _holding[(DeltaRegisters.DriveUnit, DeltaRegisters.OutputFrequency)] = moving ? commanded : (ushort)0;
    }

    private void Record(Op op) => _ops.Add(op);

    public void Dispose() => IsConnected = false;

    /// <summary>One transaction as it reached the wire.</summary>
    internal sealed record Op(string Kind, byte Unit, ushort Address, string What,
        ChannelPriority Priority, ushort[]? Values)
    {
        public bool IsWrite => Kind.StartsWith("write", StringComparison.Ordinal);

        public ushort Value => Values is { Length: > 0 } ? Values[0] : (ushort)0;

        public bool Flag => Value != 0;

        public override string ToString() =>
            $"{Kind} u{Unit} 0x{Address:X4} [{string.Join(',', Values ?? [])}] ({What}, {Priority})";
    }
}
