using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Beutl.Benchmarks.Rendering;

internal static class BenchmarkHarnessProvenance
{
    private const string OutputPathEnvironmentVariable = "BEUTL_RENDER_BENCHMARK_HARNESS_PROVENANCE";
    internal const string BuildInputMetadataKey = "BeutlBenchmarkHarnessBuildInputSha256";

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
        SortedDictionary<string, string> buildInputSha256 = ReadBuildInputSha256(assembly);
        string buildInputBundleSha256 = CalculateBuildInputBundleSha256(buildInputSha256);
        string assemblyPath = assembly.Location;
        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
            throw new InvalidOperationException("The benchmark harness assembly file is unavailable.");
        string assemblySha256 = Sha256Hex(File.ReadAllBytes(assemblyPath));
        string fullPath = Path.GetFullPath(outputPath);
        string? parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);
        using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, new
        {
            SchemaVersion = 2,
            HarnessAssemblyName = assemblyName,
            HarnessAssemblyVersion = assemblyVersion,
            SourceRevision = sourceRevision,
            HarnessAssemblySha256 = assemblySha256,
            BuildInputBundleSha256 = buildInputBundleSha256,
            BuildInputSha256 = buildInputSha256,
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        });
        stream.WriteByte((byte)'\n');
    }

    internal static SortedDictionary<string, string> ReadBuildInputSha256(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (AssemblyMetadataAttribute attribute in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (!string.Equals(attribute.Key, BuildInputMetadataKey, StringComparison.Ordinal))
                continue;
            KeyValuePair<string, string> input = ParseBuildInputMetadataValue(attribute.Value);
            if (!result.TryAdd(input.Key, input.Value))
                throw new InvalidOperationException($"Duplicate benchmark harness build input: {input.Key}");
        }
        if (result.Count == 0)
            throw new InvalidOperationException("The benchmark harness assembly has no build-input provenance.");
        return result;
    }

    internal static KeyValuePair<string, string> ParseBuildInputMetadataValue(string? value)
    {
        if (value is null)
            throw new InvalidOperationException("A benchmark harness build-input attribute has no value.");
        int separator = value.LastIndexOf('|');
        if (separator <= 0 || separator == value.Length - 1)
        {
            throw new InvalidOperationException(
                $"Benchmark harness build-input attribute is malformed: {value}");
        }
        string path = value[..separator];
        string hash = value[(separator + 1)..].ToLowerInvariant();
        ValidateBuildInputPath(path);
        ValidateSha256(hash, $"build input '{path}'");
        return KeyValuePair.Create(path, hash);
    }

    internal static string CalculateBuildInputBundleSha256(
        IReadOnlyDictionary<string, string> buildInputSha256)
    {
        ArgumentNullException.ThrowIfNull(buildInputSha256);
        using var payload = new MemoryStream();
        foreach ((string path, string hash) in buildInputSha256.OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            ValidateBuildInputPath(path);
            ValidateSha256(hash, $"build input '{path}'");
            payload.Write(Encoding.UTF8.GetBytes(path));
            payload.WriteByte(0);
            payload.Write(Encoding.ASCII.GetBytes(hash.ToLowerInvariant()));
            payload.WriteByte((byte)'\n');
        }
        if (payload.Length == 0)
            throw new InvalidOperationException("The benchmark harness build-input map is empty.");
        return Sha256Hex(payload.ToArray());
    }

    internal static string ExtractSourceRevision(string assemblyVersion)
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

    private static void ValidateBuildInputPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || Path.IsPathRooted(path)
            || path.Contains('\\', StringComparison.Ordinal)
            || path.Contains('|', StringComparison.Ordinal)
            || path.Split('/').Any(static part => part is "" or "." or ".."))
        {
            throw new InvalidOperationException($"Benchmark harness build-input path is unsafe: {path}");
        }
    }

    private static void ValidateSha256(string value, string label)
    {
        if (value.Length != 64 || value.Any(static item => !Uri.IsHexDigit(item)))
            throw new InvalidOperationException($"Benchmark harness {label} SHA-256 is invalid.");
    }

    private static string Sha256Hex(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
