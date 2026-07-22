using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;

namespace Beutl.Benchmarks.Rendering;

/// <summary>
/// Shared BenchmarkDotNet policy for paired persistent-lifetime render-pipeline measurements.
/// Renderer warm-up and output/counter verification remain GlobalSetup responsibilities of the benchmark class.
/// </summary>
internal sealed class RenderPipelineBenchmarkConfig : ManualConfig
{
    public const string ArtifactsPathEnvironmentVariable = "BEUTL_RENDER_BENCHMARK_ARTIFACTS";

    public const string CountersPathEnvironmentVariable = "BEUTL_RENDER_BENCHMARK_COUNTERS";

    public const int SetupWarmupFrameCount = 5;

    public const int BenchmarkWarmupCount = 3;

    public const int BenchmarkIterationCount = 15;

    public const string LifetimeContract =
        "persistent-root-pipeline-and-version-available-structural-program-render-cache-target-pool-state";

    public RenderPipelineBenchmarkConfig()
    {
        AddJob(Job.Default
            .WithId("RenderPipeline")
            .WithStrategy(RunStrategy.Monitoring)
            .WithLaunchCount(1)
            .WithWarmupCount(BenchmarkWarmupCount)
            .WithIterationCount(BenchmarkIterationCount)
            .WithInvocationCount(1)
            .WithUnrollFactor(1));
        AddDiagnoser(MemoryDiagnoser.Default);
        AddColumnProvider(DefaultColumnProviders.Instance);
        AddLogger(ConsoleLogger.Default);
        AddExporter(JsonExporter.Full);

        string? artifactsPath = Environment.GetEnvironmentVariable(ArtifactsPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(artifactsPath))
        {
            ArtifactsPath = Path.GetFullPath(artifactsPath);
        }

        string? countersPath = Environment.GetEnvironmentVariable(CountersPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(countersPath))
        {
            string root = !string.IsNullOrWhiteSpace(artifactsPath)
                ? Path.GetFullPath(artifactsPath)
                : Path.GetFullPath("BenchmarkDotNet.Artifacts");
            Environment.SetEnvironmentVariable(
                CountersPathEnvironmentVariable,
                Path.Combine(root, "render-pipeline-counters"));
        }
    }

    public static string GetCountersPath()
    {
        string? value = Environment.GetEnvironmentVariable(CountersPathEnvironmentVariable);
        return !string.IsNullOrWhiteSpace(value)
            ? Path.GetFullPath(value)
            : throw new InvalidOperationException(
                $"{CountersPathEnvironmentVariable} was not initialized by the benchmark configuration.");
    }
}
