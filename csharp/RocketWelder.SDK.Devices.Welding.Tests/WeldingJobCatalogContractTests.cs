using System.Reflection;
using FluentAssertions;
using RocketWelder.SDK.Abstractions;

namespace RocketWelder.SDK.Devices.Welding.Tests;

/// <summary>
/// Pins the shape of the epic-110 job-catalog contract (design.md §1). rocket-welder2 implements
/// <see cref="IWeldingJobCatalog"/> and persists the enum ordinals; the fronius plugin consumes it. Both bind to
/// THIS declaration, and a 2.27 host rides up to it (§7), so a rename, a dropped member or a reordered enum is a
/// breaking change and must be deliberate.
/// </summary>
public sealed class WeldingJobCatalogContractTests
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    [Fact]
    public void WeldingJobProbeOutcome_OrdinalsArePinned()
    {
        // Persisted by ordinal in the "WeldingJobProbes-{id}" stream (design.md §2): append only.
        ((int)WeldingJobProbeOutcome.Populated).Should().Be(0);
        ((int)WeldingJobProbeOutcome.Empty).Should().Be(1);
        ((int)WeldingJobProbeOutcome.Unreadable).Should().Be(2);
        Enum.GetValues<WeldingJobProbeOutcome>().Should().HaveCount(3);
    }

    [Fact]
    public void WeldingMode_AppendsCcCv_AtOrdinal6()
    {
        // 2026-09-24: CcCv appended for the epic-110 hardware check (owner request); ordinals are persisted, append only.
        Enum.GetNames<WeldingMode>().Should().Equal("Unknown", "MigMagStandard", "MigMagSynergic", "Job", "Tig", "Mma", "CcCv");
        Enum.GetValues<WeldingMode>().Select(m => (int)m).Should().Equal(0, 1, 2, 3, 4, 5, 6);
    }

    [Fact]
    public void IWeldingJobCatalog_DeclaresExactlyTheDesignedMethods()
    {
        var methods = typeof(IWeldingJobCatalog).GetMethods(Declared)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        methods.Should().Equal(
            "Groups",
            "HideJobAsync",
            "Job",
            "Jobs",
            "NameGroupAsync",
            "NameJobAsync",
            "RecordProbesAsync");
    }

    [Fact]
    public void IWeldingJobCatalog_DeclaresOnlyTheChangedEvent()
    {
        typeof(IWeldingJobCatalog).GetProperties(Declared).Should().BeEmpty();
        var events = typeof(IWeldingJobCatalog).GetEvents(Declared);
        events.Should().ContainSingle();
        events[0].Name.Should().Be("Changed");
        events[0].EventHandlerType.Should().Be(typeof(Action<DeviceId>));
    }

    [Theory]
    [InlineData("Groups", typeof(IReadOnlyList<WeldingJobGroup>), new[] { typeof(DeviceId) })]
    [InlineData("Jobs", typeof(IReadOnlyList<WeldingJobEntry>), new[] { typeof(DeviceId), typeof(int) })]
    [InlineData("Job", typeof(WeldingJobEntry), new[] { typeof(DeviceId), typeof(int) })]
    [InlineData("NameGroupAsync", typeof(Task), new[] { typeof(DeviceId), typeof(int), typeof(string), typeof(CancellationToken) })]
    [InlineData("NameJobAsync", typeof(Task), new[] { typeof(DeviceId), typeof(int), typeof(string), typeof(CancellationToken) })]
    [InlineData("HideJobAsync", typeof(Task), new[] { typeof(DeviceId), typeof(int), typeof(bool), typeof(CancellationToken) })]
    [InlineData("RecordProbesAsync", typeof(Task), new[] { typeof(DeviceId), typeof(IReadOnlyList<WeldingJobProbeRecord>), typeof(CancellationToken) })]
    public void IWeldingJobCatalog_MethodSignatureMatchesDesign(string name, Type returnType, Type[] parameters)
    {
        var method = typeof(IWeldingJobCatalog).GetMethod(name, parameters);

        method.Should().NotBeNull($"design.md §1 declares {name}({string.Join(", ", parameters.Select(p => p.Name))})");
        method!.ReturnType.Should().Be(returnType);
    }

    [Theory]
    [InlineData("NameGroupAsync")]
    [InlineData("NameJobAsync")]
    [InlineData("HideJobAsync")]
    [InlineData("RecordProbesAsync")]
    public void IWeldingJobCatalog_WriteMethods_CancellationTokenIsOptional(string name)
    {
        var ct = typeof(IWeldingJobCatalog).GetMethod(name)!.GetParameters().Last();

        ct.ParameterType.Should().Be(typeof(CancellationToken));
        ct.IsOptional.Should().BeTrue();
    }

    [Fact]
    public void NoPublicSdkType_DeclaresAProbeMethod()
    {
        // decisions.md D-9 / test A15 (FR-4.7): probing is a member of the concrete adapter, never an SDK surface.
        var sdkTypes = typeof(IWeldingJobCatalog).Assembly.GetExportedTypes()
            .Concat(typeof(DeviceId).Assembly.GetExportedTypes());

        var offenders = sdkTypes
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => m.Name is "ProbeJobAsync" or "ScanJobsAsync")
                .Select(m => $"{t.FullName}.{m.Name}"))
            .ToArray();

        offenders.Should().BeEmpty();
        typeof(IWeldingJobCatalog).Assembly.GetExportedTypes().Select(t => t.Name).Should().NotContain("IWeldingJobProbe");
    }
}
