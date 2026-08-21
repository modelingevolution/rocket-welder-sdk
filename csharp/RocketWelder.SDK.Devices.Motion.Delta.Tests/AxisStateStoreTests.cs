namespace RocketWelder.SDK.Devices.Motion.Delta.Tests;

/// <summary>
/// The captured zero has to outlive the process, and an axis that silently forgets where zero is
/// will drive confidently to a wrong absolute angle.
/// </summary>
public class AxisStateStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(),
        $"delta-axis-state-{Guid.NewGuid():N}", "axes.json");

    [Fact]
    public async Task AStoredZeroComesBack()
    {
        var store = new JsonAxisStateStore(_path);
        await store.SaveAsync("turntable", new AxisPersistedState(-13_280, Homed: true, SpeedDegPerSecond: 12.5));

        var loaded = await new JsonAxisStateStore(_path).LoadAsync("turntable");

        loaded.Should().Be(new AxisPersistedState(-13_280, true, 12.5));
    }

    [Fact]
    public async Task AxesDoNotOverwriteEachOther()
    {
        var store = new JsonAxisStateStore(_path);
        await store.SaveAsync("tilt", new AxisPersistedState(1, true, 1));
        await store.SaveAsync("turntable", new AxisPersistedState(2, false, 2));

        (await store.LoadAsync("tilt"))!.ZeroOffset.Should().Be(1);
        (await store.LoadAsync("turntable"))!.ZeroOffset.Should().Be(2);
    }

    [Fact]
    public async Task AnUnknownAxisReadsAsNothingStored()
    {
        var store = new JsonAxisStateStore(_path);
        await store.SaveAsync("tilt", new AxisPersistedState(1, true, 1));

        (await store.LoadAsync("turntable")).Should().BeNull();
    }

    [Fact]
    public async Task ACorruptFileForcesAReHome_RatherThanTrustingRubbish()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await File.WriteAllTextAsync(_path, "{ this is not json");

        (await new JsonAxisStateStore(_path).LoadAsync("turntable")).Should().BeNull(
            "'we do not know where zero is' must read as unhomed, which is the safe answer");
    }

    [Fact]
    public async Task NoTemporaryFileIsLeftBehind()
    {
        // The swap-in is what makes the write atomic; a leftover .tmp means the move never happened.
        var store = new JsonAxisStateStore(_path);
        await store.SaveAsync("turntable", new AxisPersistedState(1, true, 1));

        File.Exists(_path + ".tmp").Should().BeFalse();
        File.Exists(_path).Should().BeTrue();
    }

    [Fact]
    public async Task AnAxisRestoresItsZeroOnInitialise_WithoutRehoming()
    {
        var store = new InMemoryAxisStateStore();
        await store.SaveAsync(DeltaPositionerDefaults.TiltAxisName,
            new AxisPersistedState(41_730, Homed: true, SpeedDegPerSecond: 5.0));

        using var bed = AxisTestBed.Build(DeltaPositionerDefaults.Tilt, store: store,
            arrange: d => d.PositionCounts = 41_730);
        await bed.Axis.InitialiseAsync(CancellationToken.None);
        await bed.Axis.PowerAsync(true);

        bed.Axis.IsHomed.Should().BeTrue();
        (await bed.Axis.ReadStatusAsync()).Position.Should().BeApproximately(0.0, 1e-9);
    }

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_path);
        if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
