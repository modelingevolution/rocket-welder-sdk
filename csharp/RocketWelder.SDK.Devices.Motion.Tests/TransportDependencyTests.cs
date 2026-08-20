using System.Reflection;
using FluentAssertions;

namespace RocketWelder.SDK.Devices.Motion.Tests;

/// <summary>
/// <b>AC-26 / NFR-4</b> — the contract package carries no transport dependency: an automation
/// program that uses the motion contract must not drag in a fieldbus library. This is what keeps
/// the adapter substitutable and the contract vendor-neutral.
/// </summary>
public class TransportDependencyTests
{
    private static readonly Assembly Contract = typeof(IMotionAxis).Assembly;

    /// <summary>Substrings that name a wire protocol or a fieldbus stack, matched case-insensitively.</summary>
    private static readonly string[] TransportMarkers =
    [
        "modbus", "fluentmodbus", "canopen", "ethercat", "profinet", "opcua", "opc.ua",
        "s7net", "sharp7", "libplctag", "nmodbus", "easymodbus", "snap7",
        "system.io.ports", "serialport", "sockets", "grpc", "mqtt",
    ];

    [Fact]
    public void Contract_ReferencesNoTransportAssembly_Transitively()
    {
        var offenders = ClosureOf(Contract)
            .Where(n => TransportMarkers.Any(m => n.Contains(m, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        offenders.Should().BeEmpty(
            "NFR-4: a program using the motion contract must not drag in a Modbus library");
    }

    [Fact]
    public void Contract_DirectReferences_AreExactlyTheDeclaredOnes()
    {
        // A blocklist only catches transports someone thought of. Pinning the direct references
        // catches every new dependency, transport or not, the moment it is added.
        var direct = Contract.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => !IsFrameworkAssembly(n))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        direct.Should().Equal(
            "ModelingEvolution.Drawing",                 // the typed units
            "ModelingEvolution.Signals",                 // NOT used by the contract: inherited via
                                                         // MotionDeviceTypeInfo : DeviceTypeInfo,
                                                         // whose GetSignals is typed ISignal<float>
            "RocketWelder.SDK.Abstractions",             // IDevice / DeviceId
            "RocketWelder.SDK.Automation.Abstractions"); // ConfigPropertySchema + DeviceTypeInfo
    }

    [Fact]
    public void Control_TheAllowlistDoesNotWaveThroughATransportWithAFrameworkShapedName()
    {
        // The specific hole an IsFrameworkAssembly prefix-guess would leave open. System.IO.Ports
        // is a serial transport shipped as its own package; a StartsWith("System") rule would
        // classify it as framework and hide it from both checks below.
        IsFrameworkAssembly("System.IO.Ports").Should().BeFalse(
            "it is a NuGet-shipped serial transport, not part of the shared framework");

        // ...while genuine shared-framework assemblies stay classified as framework, including the
        // System.Net.* ones a name-based rule would have to special-case by hand.
        IsFrameworkAssembly("System.Runtime").Should().BeTrue();
        IsFrameworkAssembly("System.Net.Sockets").Should().BeTrue(
            "it ships in Microsoft.NETCore.App and is present in every .NET process");

        // And the allowlist must be a real enumeration, not an empty set that silently allows all.
        SharedFrameworkAssemblies.Should().NotBeEmpty();
    }

    [Fact]
    public void Control_TheHarnessCanSeeReferencesAtAll()
    {
        // Without this, "no transport reference found" could simply mean "no reference found".
        ClosureOf(Contract).Should().Contain("ModelingEvolution.Drawing");

        // ...and the marker matcher must be able to say yes to something.
        TransportMarkers.Should().Contain(m => "FluentModbus".Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The .NET shared framework, enumerated from the runtime directory rather than guessed from
    /// names. This is the ALLOWLIST that replaced a <c>StartsWith("System")</c> prefix rule.
    ///
    /// <para>
    /// A prefix rule is the hole this check exists to close: <c>System.IO.Ports</c> is a
    /// separately-shipped NuGet package and a serial-line transport, and <c>"System"</c> would wave
    /// it straight through. Classifying by <b>where the assembly actually lives</b> is ground truth
    /// instead of a naming convention — a shared-framework assembly sits in
    /// <c>Microsoft.NETCore.App</c> and is present in every .NET process no matter what this package
    /// references, whereas a NuGet transport is copied next to the application and is exactly the
    /// "dragged in" dependency NFR-4 forbids.
    /// </para>
    ///
    /// <para>
    /// Worked example, measured rather than assumed: <c>System.Net.Sockets</c> IS in this set —
    /// it reaches the closure through <c>System.Net.Security</c>, deep inside the BCL, from
    /// <c>Microsoft.NETCore.App/10.0.8</c>. Flagging it would be a false positive that teaches
    /// people to suppress the check. <c>System.IO.Ports</c> is NOT in this set and would be
    /// flagged, which is the case that matters.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> SharedFrameworkAssemblies = LoadSharedFramework();

    private static HashSet<string> LoadSharedFramework()
    {
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return Directory.EnumerateFiles(runtimeDir, "*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToHashSet(StringComparer.Ordinal)!;
    }

    private static bool IsFrameworkAssembly(string name) => SharedFrameworkAssemblies.Contains(name);

    /// <summary>Every assembly reachable from <paramref name="root"/> by compile-time reference.</summary>
    private static HashSet<string> ClosureOf(Assembly root)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<Assembly>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            foreach (var reference in queue.Dequeue().GetReferencedAssemblies())
            {
                if (!seen.Add(reference.Name!)) continue;
                if (IsFrameworkAssembly(reference.Name!)) continue;
                try { queue.Enqueue(Assembly.Load(reference)); }
                catch (Exception e) when (e is FileNotFoundException or BadImageFormatException) { /* name recorded; body unavailable */ }
            }
        }

        return seen;
    }
}
