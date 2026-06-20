using System.Collections.Immutable;
using System.Reflection;

using Beutl.Engine.SourceGenerators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SourceGeneratorTest;

/// <summary>
/// Holds the generator diagnostics and the generated sources (keyed by hint name) from a single
/// harness run, with convenience lookups for assertions.
/// </summary>
internal sealed record GeneratorHarnessResult(
    ImmutableArray<Diagnostic> GeneratorDiagnostics,
    IReadOnlyDictionary<string, string> GeneratedSources)
{
    public string GetSource(string hintNameSuffix)
    {
        foreach (KeyValuePair<string, string> kvp in GeneratedSources)
        {
            if (kvp.Key.EndsWith(hintNameSuffix, StringComparison.Ordinal))
            {
                return kvp.Value;
            }
        }

        throw new AssertionException(
            $"No generated source whose hint name ends with '{hintNameSuffix}'. "
            + $"Generated: [{string.Join(", ", GeneratedSources.Keys)}]");
    }

    public bool HasSource(string hintNameSuffix)
        => GeneratedSources.Keys.Any(k => k.EndsWith(hintNameSuffix, StringComparison.Ordinal));
}

/// <summary>
/// Builds an in-memory compilation from the embedded generator inputs/stubs and runs the
/// engine source generators through a <see cref="CSharpGeneratorDriver"/>, exposing the
/// generated sources for assertion.
/// </summary>
internal static class GeneratorDriverHarness
{
    /// <summary>
    /// Minimal supplementary stubs that the embedded 4-file framework does not provide but the
    /// analysis phase of the generators looks up by metadata name. Without these the
    /// EngineObjectResourceGenerator bails out early and emits nothing.
    /// </summary>
    private const string SupplementaryStubs = """
        namespace Beutl.Engine
        {
            public interface IListProperty<T> { }

            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Property)]
            public sealed class SuppressResourceClassGenerationAttribute : System.Attribute { }
        }
        """;

    /// <summary>
    /// Runs both engine source generators against the embedded inputs (Class1.cs + the 4 stub
    /// files) plus the supplementary stubs. Optionally appends extra inline sources for
    /// scenario-specific tests.
    /// </summary>
    public static GeneratorHarnessResult Run(params string[] extraSources)
    {
        var sources = new List<string>
        {
            ReadEmbedded("GeneratorInputs/Class1.cs"),
            ReadEmbedded("GeneratorInputs/EngineObject.cs"),
            ReadEmbedded("GeneratorInputs/IProperty.cs"),
            ReadEmbedded("GeneratorInputs/Property.cs"),
            ReadEmbedded("GeneratorInputs/RenderContext.cs"),
            SupplementaryStubs,
        };
        sources.AddRange(extraSources);

        var syntaxTrees = sources
            .Select(s => CSharpSyntaxTree.ParseText(s))
            .ToArray();

        // GetAssemblies() only returns assemblies already loaded into the AppDomain, which can miss ones
        // the inline test scenarios reference (e.g. System.Text.Json via the IFallback/JsonObject case,
        // System.Collections.Immutable) and cause intermittent "type not found" failures. Force-load and
        // explicitly seed those references so the compilation is deterministic regardless of load order.
        _ = typeof(System.Text.Json.Nodes.JsonObject);
        _ = typeof(System.Collections.Immutable.ImmutableArray);

        var seededLocations = new[]
        {
            typeof(object).Assembly.Location,
            typeof(System.Text.Json.Nodes.JsonObject).Assembly.Location,
            typeof(System.Collections.Immutable.ImmutableArray).Assembly.Location,
        };

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => a.Location)
            .Concat(seededLocations)
            .Distinct(StringComparer.Ordinal)
            .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "SourceGeneratorTest.GeneratedAssembly",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(
            new IIncrementalGenerator[] { new EngineObjectResourceGenerator(), new FallbackTypeGenerator() });

        GeneratorDriver ran = driver.RunGeneratorsAndUpdateCompilation(
            compilation, out _, out ImmutableArray<Diagnostic> diagnostics);

        GeneratorDriverRunResult runResult = ran.GetRunResult();

        var generated = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (GeneratorRunResult result in runResult.Results)
        {
            foreach (GeneratedSourceResult source in result.GeneratedSources)
            {
                generated[source.HintName] = source.SourceText.ToString();
            }
        }

        return new GeneratorHarnessResult(diagnostics, generated);
    }

    private static string ReadEmbedded(string logicalName)
    {
        Assembly assembly = typeof(GeneratorDriverHarness).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{logicalName}' not found. Available: "
                + $"[{string.Join(", ", assembly.GetManifestResourceNames())}]");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
