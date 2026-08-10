using System.Reflection;
using System.Text.Json;

namespace Beutl.GpuPassTargetBenchmarkHarness;

internal static class BenchmarkHarnessProvenance
{
    private const string OutputPathEnvironmentVariable = "BEUTL_RENDER_BENCHMARK_HARNESS_PROVENANCE";

    public static void WriteFromEnvironment(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        string? outputPath = Environment.GetEnvironmentVariable(OutputPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(outputPath))
            return;

        string assemblyName = assembly.GetName().Name
            ?? throw new InvalidOperationException("The benchmark harness assembly has no name.");
        string assemblyVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? throw new InvalidOperationException("The benchmark harness assembly has no informational version.");
        string sourceRevision = ExtractSourceRevision(assemblyVersion);
        string fullPath = Path.GetFullPath(outputPath);
        string? parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);
        using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, new
        {
            SchemaVersion = 1,
            HarnessAssemblyName = assemblyName,
            HarnessAssemblyVersion = assemblyVersion,
            SourceRevision = sourceRevision,
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        });
        stream.WriteByte((byte)'\n');
    }

    private static string ExtractSourceRevision(string assemblyVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyVersion);
        for (int start = 0; start <= assemblyVersion.Length - 40; start++)
        {
            ReadOnlySpan<char> candidate = assemblyVersion.AsSpan(start, 40);
            if (candidate.ToString().All(Uri.IsHexDigit))
            {
                return candidate.ToString().ToLowerInvariant();
            }
        }

        throw new InvalidOperationException(
            $"Benchmark harness assembly version '{assemblyVersion}' does not contain a 40-character source revision.");
    }
}
