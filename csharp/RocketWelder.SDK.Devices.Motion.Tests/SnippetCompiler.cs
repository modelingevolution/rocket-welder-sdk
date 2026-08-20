using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RocketWelder.SDK.Devices.Motion.Tests;

/// <summary>
/// The harness behind AC-21: compiles a C# snippet <b>against the real contract assemblies</b> —
/// the same <c>RocketWelder.SDK.Devices.Motion.dll</c> and <c>ModelingEvolution.Drawing.dll</c>
/// this test project references — and reports the compiler's own diagnostics.
///
/// <para>
/// References come from the runtime's TRUSTED_PLATFORM_ASSEMBLIES list, i.e. exactly what this test
/// process was published with. Nothing is stubbed: a snippet that compiles here compiles in a real
/// automation program, and a snippet that does not, does not.
/// </para>
///
/// <para>
/// Every negative case is paired with a <b>positive twin</b> compiled through this same method. A
/// negative-compilation suite whose harness is quietly broken passes trivially — the twin is the
/// control that proves the harness can still say "yes".
/// </para>
/// </summary>
internal static class SnippetCompiler
{
    private static readonly ImmutableArray<MetadataReference> References = LoadReferences();

    private static ImmutableArray<MetadataReference> LoadReferences()
    {
        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!File.Exists(path)) continue;
            try { builder.Add(MetadataReference.CreateFromFile(path)); }
            catch (BadImageFormatException) { /* native or resource-only dll on the TPA list */ }
        }
        return builder.ToImmutable();
    }

    /// <summary>
    /// Compiles <paramref name="body"/> as the body of a method inside a class, with the contract's
    /// namespaces imported, and returns the compiler errors (warnings excluded).
    /// </summary>
    public static ImmutableArray<Diagnostic> Compile(string body)
    {
        var source = $$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using ModelingEvolution.Drawing;
            using ModelingEvolution.Drawing.Units;
            using RocketWelder.SDK.Devices.Motion;

            internal static class Snippet
            {
                public static async Task Run(IRotaryAxis rotary, ILinearAxis linear)
                {
            {{body}}
                }
            }
            """;

        var compilation = CSharpCompilation.Create(
            assemblyName: "Snippet_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        return compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
    }

    /// <summary>The distinct compiler error ids raised by <paramref name="body"/>.</summary>
    public static string[] ErrorIds(string body) =>
        Compile(body).Select(d => d.Id).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToArray();

    /// <summary>A human-readable dump used in assertion messages when a case behaves unexpectedly.</summary>
    public static string Describe(string body) =>
        string.Join(Environment.NewLine, Compile(body).Select(d => d.ToString()));
}
