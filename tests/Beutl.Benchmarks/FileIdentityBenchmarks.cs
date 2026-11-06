using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;

using Beutl.Benchmarks.Rendering;
using Beutl.Media;

namespace Beutl.Benchmarks;

internal sealed class FileIdentityBenchmarkConfig : ManualConfig
{
    public FileIdentityBenchmarkConfig()
    {
        AddJob(Job.Default
            .WithStrategy(RunStrategy.Throughput)
            .WithWarmupCount(3)
            .WithIterationCount(5)
            .WithArguments(RenderPipelineBenchmarkConfig.CreateSkiaSharpArguments()));
        AddDiagnoser(MemoryDiagnoser.Default);
    }
}

/// <summary>
/// What one file event costs a watcher that compares paths by identity: listing the folders along the path, so that the
/// on-disk spelling of each name can be found.
/// </summary>
[Config(typeof(FileIdentityBenchmarkConfig))]
public class CanonicalPathBenchmarks
{
    private string _directory = string.Empty;
    private string _file = string.Empty;

    [Params(10, 1000, 10000)]
    public int Siblings { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"beutl-benchmark-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        for (int index = 0; index < Siblings; index++)
            File.WriteAllBytes(Path.Combine(_directory, $"{index:D6}.png"), []);
        _file = Path.Combine(_directory, $"{Siblings / 2:D6}.png");
    }

    [GlobalCleanup]
    public void Cleanup() => Directory.Delete(_directory, recursive: true);

    [Benchmark]
    public bool CompareTwoPaths() => FilePathComparison.AreSameCanonicalPath(_file, _file);
}

/// <summary>Cold start's walk of a font tree, which resolves each folder to stop at a symbolic link cycle.</summary>
[Config(typeof(FileIdentityBenchmarkConfig))]
public class FontDirectoryScanBenchmarks
{
    private const int FontsPerFolder = 8;
    private string _root = string.Empty;

    [Params(50, 500)]
    public int Folders { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), $"beutl-benchmark-{Guid.NewGuid():N}");
        for (int folder = 0; folder < Folders; folder++)
        {
            string directory = Path.Combine(_root, $"family{folder % 10}", $"style{folder}");
            Directory.CreateDirectory(directory);
            for (int font = 0; font < FontsPerFolder; font++)
                File.WriteAllBytes(Path.Combine(directory, $"font{font}.ttf"), []);

            // Distributions alias a family under another name, as /usr/share/fonts does.
            if (!OperatingSystem.IsWindows() && folder % 10 == 0)
                Directory.CreateSymbolicLink(Path.Combine(_root, $"alias{folder}"), directory);
        }

        // A first use scans the system's fonts in its static constructor, which is not what this measures.
        _ = FontManager.EnumerateFontCandidates(_root).Count();
    }

    [GlobalCleanup]
    public void Cleanup() => Directory.Delete(_root, recursive: true);

    [Benchmark(Baseline = true)]
    public int WalkWithoutIdentities()
        => Directory.EnumerateFiles(_root, "*.ttf", new EnumerationOptions { RecurseSubdirectories = true }).Count();

    [Benchmark]
    public int EnumerateFontCandidates() => FontManager.EnumerateFontCandidates(_root).Count();
}
