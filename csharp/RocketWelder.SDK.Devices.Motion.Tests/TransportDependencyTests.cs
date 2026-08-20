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
    public void Control_TheHarnessCanSeeReferencesAtAll()
    {
        // Without this, "no transport reference found" could simply mean "no reference found".
        ClosureOf(Contract).Should().Contain("ModelingEvolution.Drawing");

        // ...and the marker matcher must be able to say yes to something.
        TransportMarkers.Should().Contain(m => "FluentModbus".Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsFrameworkAssembly(string name) =>
        name is "netstandard" or "mscorlib" ||
        name.StartsWith("System", StringComparison.Ordinal) ||
        name.StartsWith("Microsoft.", StringComparison.Ordinal);

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
