using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;

using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;

namespace Beutl.Benchmarks.Rendering;

/// <summary>
/// Shared BenchmarkDotNet policy for paired persistent-lifetime render-pipeline measurements.
/// Renderer warm-up and output/counter verification remain GlobalSetup responsibilities of the benchmark class.
/// </summary>
internal sealed class RenderPipelineBenchmarkConfig : ManualConfig
{
    public const string ArtifactsPathEnvironmentVariable = "BEUTL_RENDER_BENCHMARK_ARTIFACTS";

    public const string CountersPathEnvironmentVariable = "BEUTL_RENDER_BENCHMARK_COUNTERS";

    /// <summary>Selects the renderer's fusion mode, so one binary can supply both sides of a paired run.</summary>
    /// <remarks>
    /// Accepts <c>Enabled</c> (the default) or <c>Disabled</c>. A run with fusion disabled is this branch's
    /// renderer with the optimizer switched off, which is a weaker baseline than the pre-feature renderer;
    /// <see cref="Beutl.Evidence.PairedBenchmarkManifest.ComparisonMode"/> records which one produced a manifest.
    /// </remarks>
    public const string FusionModeEnvironmentVariable = "BEUTL_RENDER_BENCHMARK_FUSION_MODE";


    public const int SetupWarmupFrameCount = 5;

    public const int BenchmarkWarmupCount = 3;

    public const int BenchmarkIterationCount = 15;

    public const int BenchmarkLaunchCount = 1;

    public const int BenchmarkInvocationCount = 1;

    public const int BenchmarkUnrollFactor = 1;

    public const string BenchmarkJobId = "RenderPipeline";

    public static string ExpectedJobDisplay =>
        $"{BenchmarkJobId}(InvocationCount={BenchmarkInvocationCount}, "
        + $"IterationCount={BenchmarkIterationCount}, LaunchCount={BenchmarkLaunchCount}, "
        + $"RunStrategy={RunStrategy.Monitoring}, UnrollFactor={BenchmarkUnrollFactor}, "
        + $"WarmupCount={BenchmarkWarmupCount})";

    public const string LifetimeContract =
        "persistent-root-pipeline-and-version-available-structural-program-render-cache-target-pool-state";

    public const string RequestShapeContract = "complete-target-surface-request-with-rgba16f-readback";

    public RenderPipelineBenchmarkConfig()
    {
        AddJob(Job.Default
            .WithId(BenchmarkJobId)
            .WithStrategy(RunStrategy.Monitoring)
            .WithLaunchCount(BenchmarkLaunchCount)
            .WithWarmupCount(BenchmarkWarmupCount)
            .WithIterationCount(BenchmarkIterationCount)
            .WithInvocationCount(BenchmarkInvocationCount)
            .WithUnrollFactor(BenchmarkUnrollFactor));
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

    /// <summary>The fusion mode this process measures, defaulting to the production <c>Enabled</c>.</summary>
    public static FusionMode GetFusionMode()
    {
        string? value = Environment.GetEnvironmentVariable(FusionModeEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(value))
            return FusionMode.Enabled;
        return Enum.TryParse(value, ignoreCase: true, out FusionMode mode) && Enum.IsDefined(mode)
            ? mode
            : throw new InvalidOperationException(
                $"{FusionModeEnvironmentVariable} must be 'Enabled' or 'Disabled', not '{value}'.");
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
