using System.Security.Cryptography;
using System.Text.Json;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;

using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

using SkiaSharp;

using Bitmap = Beutl.Media.Bitmap;

namespace Beutl.GpuPassTargetBenchmarkHarness;

[Config(typeof(TargetRenderPipelineBenchmarkConfig))]
public class TargetRenderPipelineBenchmarks
{
    private TargetRenderPipelineBenchmarkSession? _session;

    public static IEnumerable<string> SceneNames
        => TargetRenderPipelineScenes.All.Select(static scene => scene.Name);

    [ParamsSource(nameof(SceneNames))]
    public string CaseName { get; set; } = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _session = RenderThread.Dispatcher.Invoke(() => new TargetRenderPipelineBenchmarkSession(CaseName));
        RenderThread.Dispatcher.Invoke(_session.WarmAndVerify);
    }

    [Benchmark]
    public ulong RenderCompleteTargetRequest()
    {
        TargetRenderPipelineBenchmarkSession session = _session
            ?? throw new InvalidOperationException("Benchmark setup did not create a target session.");
        return RenderThread.Dispatcher.Invoke(session.RenderMeasuredFrame);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        TargetRenderPipelineBenchmarkSession? session = Interlocked.Exchange(ref _session, null);
        if (session is null)
            return;

        TargetRenderPipelineCounterRecord record = RenderThread.Dispatcher.Invoke(() =>
        {
            try
            {
                return session.CreateCounterRecord();
            }
            finally
            {
                session.Dispose();
            }
        });
        string directory = TargetRenderPipelineBenchmarkConfig.GetCountersPath();
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, CaseName + ".json");
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, record, TargetRenderPipelineCounterRecord.JsonOptions);
        stream.WriteByte((byte)'\n');
    }
}

internal sealed class TargetRenderPipelineBenchmarkSession : IDisposable
{
    private readonly TargetRenderPipelineSceneDefinition _scene;
    private readonly TargetSceneFixture _fixture;
    private readonly RenderNodeProcessor _processor;
    private readonly TargetEvidenceFingerprint _fingerprint;
    private int _nextFrame;
    private TargetObservedFrame? _lastSetupFrame;
    private SortedDictionary<string, long> _lastMeasuredCounters = new(StringComparer.Ordinal);
    private bool _disposed;

    public TargetRenderPipelineBenchmarkSession(string caseName)
    {
        RenderThread.Dispatcher.VerifyAccess();
        _scene = TargetRenderPipelineScenes.Get(caseName);
        IGraphicsContext graphicsContext = GraphicsContextFactory.GetOrCreateShared()
            ?? throw new InvalidOperationException("A real graphics context is required for the starting-SHA benchmark.");
        _fingerprint = TargetEvidenceFingerprint.Capture(graphicsContext);
        _fixture = TargetSceneFactory.Create(_scene);
        if (_scene.HasStaticPrefixCache)
        {
            RenderNode cacheNode = _fixture.CacheNode
                ?? throw new InvalidOperationException("The static-prefix fixture has no cache boundary.");
            cacheNode.Cache.ReportRenderCount(RenderNodeCache.Count);
            RenderNodeCacheHelper.MakeCache(cacheNode, RenderCacheOptions.Default, 1, 1);
            if (!cacheNode.Cache.IsCached)
                throw new InvalidOperationException("The starting-SHA static prefix was not cached.");
        }
        _processor = new RenderNodeProcessor(_fixture.Root, _scene.HasStaticPrefixCache, 1, 1);
    }

    public void WarmAndVerify()
    {
        ThrowIfDisposed();
        int[] frames = _scene.Animation == TargetBenchmarkAnimation.StructuralToggle
            ? [0, 1, 7, 8, 9]
            : Enumerable.Range(0, TargetRenderPipelineBenchmarkConfig.SetupWarmupFrameCount).ToArray();
        var observed = new List<TargetObservedFrame>(frames.Length);
        foreach (int frame in frames)
            observed.Add(RenderAndObserve(frame, verifyOutput: true));

        TargetObservedFrame first = observed[0];
        if (first.Width <= 0 || first.Height <= 0 || first.Energy <= 1)
            throw new InvalidOperationException($"Target scene '{_scene.Name}' produced vacuous setup output.");
        if (observed.Any(item => item.Bounds != first.Bounds
                                 || item.Width != first.Width
                                 || item.Height != first.Height))
        {
            throw new InvalidOperationException($"Target scene '{_scene.Name}' did not preserve stable bounds.");
        }

        int distinct = observed.Select(static item => item.Sha256).Distinct(StringComparer.Ordinal).Count();
        bool animated = _scene.Animation != TargetBenchmarkAnimation.None;
        if ((animated && distinct < 2) || (!animated && distinct != 1))
            throw new InvalidOperationException($"Target scene '{_scene.Name}' output did not match its animation mode.");

        _lastSetupFrame = observed[^1];
        _nextFrame = checked(frames[^1] + 1);
    }

    public ulong RenderMeasuredFrame()
    {
        ThrowIfDisposed();
        TargetObservedFrame frame = RenderAndObserve(_nextFrame++, verifyOutput: false);
        _lastMeasuredCounters = frame.RequestCounters;
        return frame.Token;
    }

    public TargetRenderPipelineCounterRecord CreateCounterRecord()
    {
        ThrowIfDisposed();
        TargetObservedFrame setup = _lastSetupFrame
            ?? throw new InvalidOperationException("Target benchmark setup verification did not complete.");
        if (_lastMeasuredCounters.Count == 0)
            throw new InvalidOperationException("Target benchmark completed without a measured request.");
        return new TargetRenderPipelineCounterRecord
        {
            SchemaVersion = 2,
            CaseName = _scene.Name,
            Seed = _scene.Seed,
            Width = setup.Width,
            Height = setup.Height,
            SetupWarmupFrames = TargetRenderPipelineBenchmarkConfig.SetupWarmupFrameCount,
            Lifetime = TargetRenderPipelineBenchmarkConfig.LifetimeContract,
            RequestShape = "complete-target-surface-request-with-rgba16f-readback",
            OutputSha256 = setup.Sha256,
            OutputChecksum = setup.Checksum.ToString("x16"),
            OutputBounds = setup.Bounds,
            Fingerprint = _fingerprint,
            SetupLastRequestCounters = setup.RequestCounters,
            MeasuredLastRequestCounters = _lastMeasuredCounters,
            LastExecutionStatistics = new SortedDictionary<string, long>(StringComparer.Ordinal)
            {
                ["LegacyOperationExecutions"] = _lastMeasuredCounters["LegacyOperationExecutions"],
            },
            StructuralPlanCacheStatistics = new SortedDictionary<string, long>(StringComparer.Ordinal)
            {
                ["UnavailableOnStartingSha"] = 1,
            },
            ProgramCacheStatistics = new SortedDictionary<string, long>(StringComparer.Ordinal)
            {
                ["UnavailableOnStartingSha"] = 1,
            },
            TargetPoolStatistics = new SortedDictionary<string, long>(StringComparer.Ordinal)
            {
                ["UnavailableOnStartingSha"] = 1,
            },
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _fixture.Dispose();
        _disposed = true;
    }

    private TargetObservedFrame RenderAndObserve(int frameIndex, bool verifyOutput)
    {
        _fixture.ApplyFrameState(_scene.GetFrameState(frameIndex));
        RenderNodeOperation[] operations = _processor.PullToRoot();
        Rect bounds = operations.Aggregate(Rect.Empty, static (result, operation) => result.Union(operation.Bounds));
        PixelRect deviceBounds = PixelRect.FromRect(bounds);
        if (deviceBounds.Width <= 0 || deviceBounds.Height <= 0)
        {
            DisposeOperations(operations);
            throw new InvalidOperationException($"Target scene '{_scene.Name}' produced no operations.");
        }

        using RenderTarget target = RenderTarget.Create(deviceBounds.Width, deviceBounds.Height)
            ?? throw new InvalidOperationException("The target benchmark request could not allocate its output.");
        using (var canvas = new ImmediateCanvas(target, 1, 1, bounds.Size))
        {
            canvas.Clear();
            using (canvas.PushTransform(Matrix.CreateTranslation(-bounds.X, -bounds.Y)))
            {
                int consumed = 0;
                try
                {
                    foreach (RenderNodeOperation operation in operations)
                    {
                        operation.Render(canvas);
                        consumed++;
                        operation.Dispose();
                    }
                }
                catch
                {
                    DisposeOperations(operations.AsSpan(consumed));
                    throw;
                }
            }
        }

        using Bitmap bitmap = target.Snapshot();
        Span<ushort> components = bitmap.GetPixelSpan<ushort>();
        ulong token = components.Length == 0
            ? 0
            : ((ulong)components[0] << 48)
              | ((ulong)components[components.Length / 3] << 32)
              | ((ulong)components[components.Length * 2 / 3] << 16)
              | components[^1];
        var counters = new SortedDictionary<string, long>(StringComparer.Ordinal)
        {
            ["CompletedRequests"] = 1,
            ["LegacyOperationExecutions"] = operations.Length,
            ["SemanticStages"] = _scene.SemanticStageCount,
            ["TopLevelDrawables"] = _scene.TopLevelDrawableCount,
            ["TargetDependencies"] = _scene.HasTargetDependencies ? _scene.TopLevelDrawableCount : 0,
            ["RenderCacheHits"] = _scene.HasStaticPrefixCache ? 1 : 0,
            ["Failures"] = 0,
        };
        if (!verifyOutput)
            return new TargetObservedFrame(bounds, bitmap.Width, bitmap.Height, token, 0, string.Empty, 0, counters);

        byte[] bytes = bitmap.GetPixelSpan().ToArray();
        string sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        ulong checksum = CalculateChecksum(components);
        double energy = 0;
        for (int index = 0; index < components.Length; index += 17)
            energy += Math.Abs((float)BitConverter.UInt16BitsToHalf(components[index]));
        return new TargetObservedFrame(bounds, bitmap.Width, bitmap.Height, token, checksum, sha256, energy, counters);
    }

    private static ulong CalculateChecksum(ReadOnlySpan<ushort> components)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        ulong result = offset;
        for (int index = 0; index < components.Length; index += 13)
        {
            result ^= components[index];
            result *= prime;
        }
        return result;
    }

    private static void DisposeOperations(ReadOnlySpan<RenderNodeOperation> operations)
    {
        foreach (RenderNodeOperation operation in operations)
        {
            try
            {
                operation.Dispose();
            }
            catch
            {
                // Preserve the active request failure while sweeping every remaining legacy operation.
            }
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

internal static class TargetSceneFactory
{
    private static readonly Rect s_domain = new(0, 0, 384, 216);

    public static TargetSceneFixture Create(TargetRenderPipelineSceneDefinition scene)
    {
        return scene.Name switch
        {
            "NoEffectControl" => Fixture(Source(scene, s_domain)),
            "SingleShader" => ShaderFixture(scene, [TargetShaderKind.Gamma]),
            "ShaderOpacityShader" => Primary(scene, barrier: false),
            "ShaderOpacityShaderBarrier" => Primary(scene, barrier: true),
            "LongInvariantChain" => LongChain(scene),
            "ParameterOnlyAnimation" => Animated(scene),
            "StructuralToggle" => Structural(scene),
            "StaticPrefixAnimatedTail" => StaticPrefix(scene),
            "MixedSpatialColor" => Mixed(scene),
            "SmallObjectFixedOverhead" => Small(scene),
            "MultipleDrawablesTargetDependencies" => Multiple(scene),
            _ => throw new ArgumentOutOfRangeException(nameof(scene), scene.Name, "Unknown target scene."),
        };
    }

    private static TargetSceneFixture Primary(TargetRenderPipelineSceneDefinition scene, bool barrier)
    {
        var fixture = new TargetSceneFixture();
        RenderNode current = Source(scene, s_domain);
        current = fixture.WrapShader(current, TargetShaderScript.Create(TargetShaderKind.Gamma));
        var opacity = new OpacityRenderNode(0.625f);
        opacity.AddChild(current);
        current = opacity;
        if (barrier)
            current = fixture.WrapShader(current, TargetShaderScript.Create(TargetShaderKind.WholeSourceIdentity));
        current = fixture.WrapShader(current, TargetShaderScript.Create(TargetShaderKind.Invert));
        fixture.Root = current;
        return fixture;
    }

    private static TargetSceneFixture ShaderFixture(
        TargetRenderPipelineSceneDefinition scene,
        IReadOnlyList<TargetShaderKind> kinds)
    {
        var fixture = new TargetSceneFixture();
        RenderNode current = Source(scene, s_domain);
        foreach (TargetShaderKind kind in kinds)
            current = fixture.WrapShader(current, TargetShaderScript.Create(kind));
        fixture.Root = current;
        return fixture;
    }

    private static TargetSceneFixture LongChain(TargetRenderPipelineSceneDefinition scene)
    {
        var kinds = Enumerable.Range(0, scene.SemanticStageCount)
            .Select(static index => (index & 1) == 0 ? TargetShaderKind.Gamma : TargetShaderKind.Invert)
            .ToArray();
        return ShaderFixture(scene, kinds);
    }

    private static TargetSceneFixture Animated(TargetRenderPipelineSceneDefinition scene)
    {
        var fixture = new TargetSceneFixture();
        RenderNode current = fixture.WrapShader(Source(scene, s_domain), TargetShaderScript.Create(TargetShaderKind.Gamma));
        var resources = Enumerable.Range(0, 60)
            .Select(index => fixture.CreateShaderResource(
                TargetShaderScript.Create(
                    TargetShaderKind.Multiply,
                    0.75f + (index / 59f * 0.5f))))
            .ToArray();
        var animated = new FilterEffectRenderNode(resources[0]);
        animated.AddChild(current);
        fixture.FrameStateConsumers.Add(state => animated.Update(resources[state.FrameModulo60]));
        fixture.Root = fixture.WrapShader(animated, TargetShaderScript.Create(TargetShaderKind.Invert));
        return fixture;
    }

    private static TargetSceneFixture Structural(TargetRenderPipelineSceneDefinition scene)
    {
        var fixture = new TargetSceneFixture();
        RenderNode current = fixture.WrapShader(Source(scene, s_domain), TargetShaderScript.Create(TargetShaderKind.Gamma));
        FilterEffect.Resource first = fixture.CreateShaderResource(TargetShaderScript.Create(TargetShaderKind.Invert));
        FilterEffect.Resource second = fixture.CreateShaderResource(TargetShaderScript.Create(TargetShaderKind.ChannelRotate));
        var toggle = new FilterEffectRenderNode(first);
        toggle.AddChild(current);
        fixture.FrameStateConsumers.Add(state => toggle.Update(state.StructuralVariant ? second : first));
        fixture.Root = fixture.WrapShader(toggle, TargetShaderScript.Create(TargetShaderKind.Invert));
        return fixture;
    }

    private static TargetSceneFixture StaticPrefix(TargetRenderPipelineSceneDefinition scene)
    {
        var fixture = new TargetSceneFixture();
        RenderNode prefix = Source(scene, s_domain);
        prefix = fixture.WrapShader(prefix, TargetShaderScript.Create(TargetShaderKind.Gamma));
        prefix = fixture.WrapShader(prefix, TargetShaderScript.Create(TargetShaderKind.Invert));
        prefix = fixture.WrapShader(prefix, TargetShaderScript.Create(TargetShaderKind.ChannelRotate));
        fixture.CacheNode = prefix;
        var resources = Enumerable.Range(0, 60)
            .Select(index => fixture.CreateShaderResource(
                TargetShaderScript.Create(
                    TargetShaderKind.Multiply,
                    0.75f + (index / 59f * 0.5f))))
            .ToArray();
        var animated = new FilterEffectRenderNode(resources[0]);
        animated.AddChild(prefix);
        fixture.FrameStateConsumers.Add(state => animated.Update(resources[state.FrameModulo60]));
        var opacity = new OpacityRenderNode(0.875f);
        opacity.AddChild(animated);
        fixture.Root = fixture.WrapShader(opacity, TargetShaderScript.Create(TargetShaderKind.ChannelRotate));
        return fixture;
    }

    private static TargetSceneFixture Mixed(TargetRenderPipelineSceneDefinition scene)
    {
        var fixture = new TargetSceneFixture();
        RenderNode current = fixture.WrapShader(Source(scene, s_domain), TargetShaderScript.Create(TargetShaderKind.Gamma));
        current = fixture.WrapShader(current, TargetShaderScript.Create(TargetShaderKind.WholeSourceIdentity));
        current = fixture.WrapShader(current, TargetShaderScript.Create(TargetShaderKind.Invert));
        var opacity = new OpacityRenderNode(0.8f);
        opacity.AddChild(current);
        fixture.Root = fixture.WrapShader(opacity, TargetShaderScript.Create(TargetShaderKind.ChannelRotate));
        return fixture;
    }

    private static TargetSceneFixture Small(TargetRenderPipelineSceneDefinition scene)
    {
        int width = Math.Max(1, (int)MathF.Round(s_domain.Width * scene.ContentScale));
        int height = Math.Max(1, (int)MathF.Round(s_domain.Height * scene.ContentScale));
        var bounds = new Rect(
            MathF.Floor((float)(s_domain.Width - width) / 2),
            MathF.Floor((float)(s_domain.Height - height) / 2),
            width,
            height);
        var fixture = new TargetSceneFixture();
        RenderNode current = fixture.WrapShader(Source(scene, bounds), TargetShaderScript.Create(TargetShaderKind.Gamma));
        var opacity = new OpacityRenderNode(0.75f);
        opacity.AddChild(current);
        fixture.Root = fixture.WrapShader(opacity, TargetShaderScript.Create(TargetShaderKind.Invert));
        return fixture;
    }

    private static TargetSceneFixture Multiple(TargetRenderPipelineSceneDefinition scene)
    {
        var fixture = new TargetSceneFixture();
        var root = new ContainerRenderNode();
        const int margin = 12;
        int width = (384 - margin * 3) / 2;
        int height = (216 - margin * 3) / 2;
        for (int index = 0; index < scene.TopLevelDrawableCount; index++)
        {
            int column = index & 1;
            int row = index >> 1;
            var bounds = new Rect(
                margin + column * (width + margin),
                margin + row * (height + margin),
                width,
                height);
            RenderNode source = fixture.WrapShader(
                Source(scene, bounds, index),
                TargetShaderScript.Create(TargetShaderKind.ChannelRotate));
            var dependency = new TargetLegacyDependencyNode();
            dependency.AddChild(source);
            root.AddChild(dependency);
        }
        fixture.Root = root;
        return fixture;
    }

    private static TargetSceneFixture Fixture(RenderNode root) => new() { Root = root };

    private static RenderNode Source(
        TargetRenderPipelineSceneDefinition scene,
        Rect bounds,
        int variant = 0)
    {
        int width = (int)bounds.Width;
        int height = (int)bounds.Height;
        RenderTarget target = RenderTarget.Create(width, height)
            ?? throw new InvalidOperationException("Could not allocate a target benchmark source.");
        using (var bitmap = new Bitmap(
                   width,
                   height,
                   BitmapColorType.RgbaF16,
                   BitmapAlphaType.Premul,
                   BitmapColorSpace.LinearSrgb))
        {
            int seed = scene.Seed + variant * 101;
            TargetRenderPipelineScenes.FillLinearPremultipliedRgba16F(bitmap.GetPixelSpan<Half>(), seed, width, height);
            using var canvas = new ImmediateCanvas(target, 1, 1, new Size(width, height));
            canvas.Clear();
            canvas.DrawBitmap(bitmap, Brushes.Resource.White, null);
        }
        return new TargetMaterializedSourceNode(target, bounds);
    }
}

internal sealed class TargetSceneFixture : IDisposable
{
    private readonly List<FilterEffect.Resource> _resources = [];

    public RenderNode Root { get; set; } = null!;

    public RenderNode? CacheNode { get; set; }

    public List<Action<TargetBenchmarkFrameState>> FrameStateConsumers { get; } = [];

    public RenderNode WrapShader(RenderNode input, string script)
    {
        FilterEffect.Resource resource = CreateShaderResource(script);
        var node = new FilterEffectRenderNode(resource);
        node.AddChild(input);
        return node;
    }

    public FilterEffect.Resource CreateShaderResource(string script)
    {
        var effect = new SKSLScriptEffect();
        effect.Script.CurrentValue = script;
        FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        _resources.Add(resource);
        return resource;
    }

    public void ApplyFrameState(TargetBenchmarkFrameState state)
    {
        foreach (Action<TargetBenchmarkFrameState> consumer in FrameStateConsumers)
            consumer(state);
    }

    public void Dispose()
    {
        Root.Dispose();
        for (int index = _resources.Count - 1; index >= 0; index--)
            _resources[index].Dispose();
    }
}

internal sealed class TargetMaterializedSourceNode(RenderTarget target, Rect bounds) : RenderNode
{
    public override RenderNodeOperation[] Process(RenderNodeContext context)
        => [RenderNodeOperation.CreateFromRenderTarget(
            bounds,
            bounds.Position,
            target.ShallowCopy(),
            EffectiveScale.At(1))];

    protected override void OnDispose(bool disposing)
    {
        target.Dispose();
        base.OnDispose(disposing);
    }
}

internal sealed class TargetLegacyDependencyNode : ContainerRenderNode
{
}

internal enum TargetShaderKind
{
    Gamma,
    Invert,
    ChannelRotate,
    Multiply,
    WholeSourceIdentity,
}

internal static class TargetShaderScript
{
    public static string Create(TargetShaderKind kind, float amount = 1)
    {
        string invariant = amount.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        return kind switch
        {
            TargetShaderKind.Gamma =>
                "uniform shader src; half4 main(float2 p) { half4 color = src.eval(p); "
                + "return half4(sqrt(max(color.rgb, half3(0))), color.a); }",
            TargetShaderKind.Invert =>
                "uniform shader src; half4 main(float2 p) { half4 color = src.eval(p); "
                + "return half4(color.a - color.rgb, color.a); }",
            TargetShaderKind.ChannelRotate =>
                "uniform shader src; half4 main(float2 p) { half4 color = src.eval(p); "
                + "return half4(color.g, color.b, color.r, color.a); }",
            TargetShaderKind.Multiply =>
                "uniform shader src; half4 main(float2 p) { half4 color = src.eval(p); "
                + $"return half4(min(color.rgb * {invariant}, color.aaa), color.a); }}",
            TargetShaderKind.WholeSourceIdentity =>
                "uniform shader src; half4 main(float2 p) { return src.eval(p); }",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }
}

internal sealed class TargetRenderPipelineBenchmarkConfig : ManualConfig
{
    public const int SetupWarmupFrameCount = 5;
    public const int BenchmarkWarmupCount = 3;
    public const int BenchmarkIterationCount = 15;
    public const string LifetimeContract =
        "persistent-root-pipeline-and-version-available-structural-program-render-cache-target-pool-state";

    public TargetRenderPipelineBenchmarkConfig()
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
        string? artifacts = Environment.GetEnvironmentVariable("BEUTL_RENDER_BENCHMARK_ARTIFACTS");
        if (!string.IsNullOrWhiteSpace(artifacts))
            ArtifactsPath = Path.GetFullPath(artifacts);
    }

    public static string GetCountersPath()
        => Path.GetFullPath(
            Environment.GetEnvironmentVariable("BEUTL_RENDER_BENCHMARK_COUNTERS")
            ?? throw new InvalidOperationException("BEUTL_RENDER_BENCHMARK_COUNTERS is not set."));
}

internal sealed class TargetRenderPipelineCounterRecord
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public int SchemaVersion { get; init; }
    public string CaseName { get; init; } = string.Empty;
    public int Seed { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int SetupWarmupFrames { get; init; }
    public string Lifetime { get; init; } = string.Empty;
    public string RequestShape { get; init; } = string.Empty;
    public string OutputSha256 { get; init; } = string.Empty;
    public string OutputChecksum { get; init; } = string.Empty;
    public Rect OutputBounds { get; init; }
    public TargetEvidenceFingerprint Fingerprint { get; init; } = new();
    public SortedDictionary<string, long> SetupLastRequestCounters { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, long> MeasuredLastRequestCounters { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, long> LastExecutionStatistics { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, long> StructuralPlanCacheStatistics { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, long> ProgramCacheStatistics { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, long> TargetPoolStatistics { get; init; } = new(StringComparer.Ordinal);
}

internal sealed record TargetObservedFrame(
    Rect Bounds,
    int Width,
    int Height,
    ulong Token,
    ulong Checksum,
    string Sha256,
    double Energy,
    SortedDictionary<string, long> RequestCounters);

internal enum TargetBenchmarkAnimation
{
    None,
    ParameterOnly,
    StructuralToggle,
}

internal readonly record struct TargetBenchmarkFrameState(int FrameModulo60, bool StructuralVariant);

internal sealed record TargetRenderPipelineSceneDefinition(
    string Name,
    int Seed,
    int SemanticStageCount,
    int TopLevelDrawableCount = 1,
    float ContentScale = 0.8f,
    TargetBenchmarkAnimation Animation = TargetBenchmarkAnimation.None,
    bool HasStaticPrefixCache = false,
    bool HasTargetDependencies = false)
{
    public TargetBenchmarkFrameState GetFrameState(int frameIndex)
        => new(
            frameIndex % 60,
            Animation == TargetBenchmarkAnimation.StructuralToggle && ((frameIndex / 8) & 1) != 0);
}

internal static class TargetRenderPipelineScenes
{
    public const int SourceSeed = 20_040_719;

    public static IReadOnlyList<TargetRenderPipelineSceneDefinition> All { get; } =
    [
        new("NoEffectControl", SourceSeed + 0, 0),
        new("SingleShader", SourceSeed + 1, 1),
        new("ShaderOpacityShader", SourceSeed + 2, 3),
        new("ShaderOpacityShaderBarrier", SourceSeed + 3, 4),
        new("LongInvariantChain", SourceSeed + 4, 10),
        new("ParameterOnlyAnimation", SourceSeed + 5, 3, Animation: TargetBenchmarkAnimation.ParameterOnly),
        new("StructuralToggle", SourceSeed + 6, 3, Animation: TargetBenchmarkAnimation.StructuralToggle),
        new("StaticPrefixAnimatedTail", SourceSeed + 7, 6,
            Animation: TargetBenchmarkAnimation.ParameterOnly, HasStaticPrefixCache: true),
        new("MixedSpatialColor", SourceSeed + 8, 5),
        new("SmallObjectFixedOverhead", SourceSeed + 9, 3, ContentScale: 0.1f),
        new("MultipleDrawablesTargetDependencies", SourceSeed + 10, 4,
            TopLevelDrawableCount: 4, HasTargetDependencies: true),
    ];

    public static TargetRenderPipelineSceneDefinition Get(string name)
        => All.SingleOrDefault(scene => string.Equals(scene.Name, name, StringComparison.Ordinal))
           ?? throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown target benchmark scene.");

    public static void FillLinearPremultipliedRgba16F(
        Span<Half> destination,
        int seed,
        int width,
        int height)
    {
        if (destination.Length != checked(width * height * 4))
            throw new ArgumentException("The target source buffer has the wrong size.", nameof(destination));
        int index = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                uint sample = MixPixel(seed, x, y);
                float alpha = (96 + ((sample >> 24) & 0x8f)) / 255f;
                destination[index++] = (Half)(((sample >> 16) & 0xff) / 255f * alpha);
                destination[index++] = (Half)(((sample >> 8) & 0xff) / 255f * alpha);
                destination[index++] = (Half)((sample & 0xff) / 255f * alpha);
                destination[index++] = (Half)alpha;
            }
        }
    }

    private static uint MixPixel(int seed, int x, int y)
    {
        unchecked
        {
            uint value = (uint)seed;
            value ^= (uint)(x / 16) * 0x9e37_79b9u;
            value ^= (uint)(y / 16) * 0x85eb_ca6bu;
            value ^= (uint)x * 0xc2b2_ae35u;
            value ^= (uint)y * 0x27d4_eb2fu;
            value ^= value >> 16;
            value *= 0x7feb_352du;
            value ^= value >> 15;
            value *= 0x846c_a68bu;
            return value ^ (value >> 16);
        }
    }
}
