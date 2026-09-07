using System.Reflection;
using FluentAssertions;
using RocketWelder.SDK.Abstractions;

namespace RocketWelder.SDK.Automation.Tests;

/// <summary>
/// Pins the shape of <see cref="IDeviceResolver"/> (epic-092 design §3.1). The interface is a published
/// plugin seam: a rename or an added member breaks every plugin compiled against it, so it must be a
/// deliberate edit here, not a side effect of a refactor.
///
/// <para>The "no vendor name" test is the SDK half of NFR-1 / RES-7 (test-scenarios §6). It reads the
/// interface's own source, embedded into this assembly by the test csproj, so it cannot silently pass
/// by looking at the wrong file: a rename of the source file fails the build of this project.</para>
/// </summary>
public sealed class DeviceResolverContractTests
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    [Fact]
    public void IDeviceResolver_DeclaresExactlyTheThreeContractMembers()
    {
        var methods = typeof(IDeviceResolver).GetMethods(Declared)
            .Where(m => !m.IsSpecialName)          // drops add_/remove_ event accessors
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        var events = typeof(IDeviceResolver).GetEvents(Declared).Select(e => e.Name).ToArray();

        methods.Should().Equal("Get", "GetAll");
        events.Should().Equal("DeviceInstanceChanged");
        typeof(IDeviceResolver).GetProperties(Declared).Should().BeEmpty();
    }

    [Fact]
    public void Get_TakesADeviceId_ReturnsTheClassConstrainedGenericArgument()
    {
        var get = typeof(IDeviceResolver).GetMethod("Get", Declared)!;

        get.GetParameters().Select(p => p.ParameterType).Should().Equal(typeof(DeviceId));
        var t = get.GetGenericArguments().Single();
        t.GenericParameterAttributes.Should().HaveFlag(GenericParameterAttributes.ReferenceTypeConstraint);
        get.ReturnType.Should().Be(t);
    }

    [Fact]
    public void GetAll_TakesNoArguments_ReturnsAnEnumerableOfTheGenericArgument()
    {
        var getAll = typeof(IDeviceResolver).GetMethod("GetAll", Declared)!;

        getAll.GetParameters().Should().BeEmpty();
        var t = getAll.GetGenericArguments().Single();
        t.GenericParameterAttributes.Should().HaveFlag(GenericParameterAttributes.ReferenceTypeConstraint);
        getAll.ReturnType.Should().Be(typeof(IEnumerable<>).MakeGenericType(t));
    }

    [Fact]
    public void DeviceInstanceChanged_CarriesTheDeviceId()
    {
        typeof(IDeviceResolver).GetEvents(Declared).Single()
            .EventHandlerType.Should().Be(typeof(Action<DeviceId>));
    }

    [Fact]
    public void TheSourceNamesNoVendor()
    {
        EmbeddedSource.Read("IDeviceResolver.cs")
            .Should().Contain("public interface IDeviceResolver",
                "positive anchor: an empty or truncated resource would satisfy the NotContain below for free")
            .And.NotContainEquivalentOf("fairino",
                "NFR-1: the instance-lookup seam is vendor-neutral (test-scenarios §6 RES-7)");
    }
}

/// <summary>Reads a product source file embedded into the test assembly by the test csproj.</summary>
internal static class EmbeddedSource
{
    public static string Read(string fileName)
    {
        var asm = typeof(EmbeddedSource).Assembly;
        var name = asm.GetManifestResourceNames().Single(n => n.EndsWith("." + fileName, StringComparison.Ordinal));
        using var s = asm.GetManifestResourceStream(name)!;
        return new StreamReader(s).ReadToEnd();
    }
}
