using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SourceGeneratorTest;

internal static class CompilationReferences
{
    // Test inputs use framework types and source stubs only. Reuse the metadata rather than
    // reopening every loaded assembly for each scenario; loaded test/adapter assemblies also
    // make the reference set depend on which test happened to run first.
    public static ImmutableArray<MetadataReference> Framework { get; } = CreateFrameworkReferences();

    private static ImmutableArray<MetadataReference> CreateFrameworkReferences()
    {
        string runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string platformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("The test runtime did not provide its framework assemblies.");

        return [.. platformAssemblies.Split(Path.PathSeparator)
            .Where(path => string.Equals(Path.GetDirectoryName(path), runtimeDirectory, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Select(path => MetadataReference.CreateFromFile(path))];
    }
}
