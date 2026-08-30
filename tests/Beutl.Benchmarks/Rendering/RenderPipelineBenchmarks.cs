using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using BenchmarkDotNet.Attributes;

using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

using Silk.NET.Vulkan;

using SkiaSharp;

using Bitmap = Beutl.Media.Bitmap;

namespace Beutl.Benchmarks.Rendering;

/// <summary>
/// Complete-request render-pipeline workloads with renderer, node, program-cache, render-cache, and target-pool
/// lifetimes that persist across setup, warm-up, and measured iterations.
/// </summary>
[Config(typeof(RenderPipelineBenchmarkConfig))]
public class RenderPipelineBenchmarks
{
    private RenderPipelineBenchmarkSession? _session;

    public static IEnumerable<string> SceneNames
        => RenderPipelineBenchmarkScenes.All.Select(static scene => scene.Name);

    [ParamsSource(nameof(SceneNames))]
    public string CaseName { get; set; } = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _session = RenderThread.Dispatcher.Invoke(() => new RenderPipelineBenchmarkSession(CaseName));
        RenderThread.Dispatcher.Invoke(_session.WarmAndVerify);
    }

    /// <summary>
    /// Renders one complete requested surface. Output validation lives in <see cref="Setup"/>, and request-wide
    /// counters and full-image hashing come from cleanup, so the measured body contains only production frame-state
    /// update, render, readback, cheap token sampling, and steady-state disposal of the preceding result.
    /// </summary>
    [Benchmark]
    public ulong RenderCompleteTargetRequest()
    {
        RenderPipelineBenchmarkSession session = _session
            ?? throw new InvalidOperationException("Benchmark setup did not create a render session.");
        return RenderThread.Dispatcher.Invoke(session.RenderMeasuredFrame);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        RenderPipelineBenchmarkSession? session = Interlocked.Exchange(ref _session, null);
        if (session is null)
            return;

        RenderPipelineBenchmarkCounterRecord record = RenderThread.Dispatcher.Invoke(() =>
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

        string directory = RenderPipelineBenchmarkConfig.GetCountersPath();
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, CaseName + ".json");
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, record, RenderPipelineBenchmarkCounterRecord.JsonOptions);
        stream.WriteByte((byte)'\n');
    }
}

internal sealed class RenderPipelineBenchmarkSession : IDisposable
{
    private static readonly Rect s_targetDomain = RenderPipelineBenchmarkScenes.TargetDomain;

    private readonly RenderPipelineBenchmarkSceneDefinition _scene;
    private readonly RenderNode _root;
    private readonly RenderNodeRenderer _renderer;
    private readonly IReadOnlyList<IFrameStateConsumer> _animatedNodes;
    private readonly IReadOnlyList<IDisposable> _sceneResources;
    private int _nextFrame;
    private RenderPipelineObservedFrame? _lastSetupFrame;
    private RenderNodeRasterization? _lastSetupRasterization;
    private int _lastSetupFrameIndex = -1;
    private RenderNodeRasterization? _lastMeasuredRasterization;
    private int _lastMeasuredFrameIndex = -1;
    private ulong _lastMeasuredToken;
    private bool _disposed;

    public RenderPipelineBenchmarkSession(string caseName)
    {
        RenderThread.Dispatcher.VerifyAccess();
        _scene = RenderPipelineBenchmarkScenes.Get(caseName);
        _ = GraphicsContextFactory.GetOrCreateShared()
            ?? throw new InvalidOperationException(
                "A real graphics context is required for render-pipeline benchmarks.");

        var animatedNodes = new List<IFrameStateConsumer>();
        var sceneResources = new List<IDisposable>();
        _root = CreateScene(_scene, animatedNodes, sceneResources);
        _animatedNodes = animatedNodes.AsReadOnly();
        _sceneResources = sceneResources.AsReadOnly();
        RenderNodeRendererOptions options = CreateRendererOptions(_scene);
        RenderPipelineInternalDiagnostics.SetPurpose(options, RenderRequestPurpose.Frame);
        _renderer = new RenderNodeRenderer(_root, options);
    }

    public void WarmAndVerify()
    {
        ThrowIfDisposed();
        RenderThread.Dispatcher.VerifyAccess();

        (int Frame, bool RetainRasterization)[] plan = GetSetupRenderPlan(_scene);
        var observed = new List<RenderPipelineObservedFrame>(plan.Length);
        foreach ((int frame, bool retainRasterization) in plan)
        {
            observed.Add(RenderAndObserve(
                frame,
                retainRasterization));
        }

        RenderPipelineObservedFrame first = observed[0];
        if (first.IsEmpty || first.Width <= 0 || first.Height <= 0
            || !double.IsFinite(first.Energy) || first.Energy <= 1)
        {
            throw new InvalidOperationException(
                $"Benchmark scene '{_scene.Name}' produced an empty, non-finite, or vacuous setup output.");
        }

        if (observed.Any(item => item.IsEmpty
                                 || item.Bounds != first.Bounds
                                 || item.Width != first.Width
                                 || item.Height != first.Height))
        {
            throw new InvalidOperationException(
                $"Benchmark scene '{_scene.Name}' did not preserve stable setup bounds and device dimensions.");
        }

        int distinctOutputs = observed.Select(static item => item.Sha256).Distinct(StringComparer.Ordinal).Count();
        bool expectsAnimation = _scene.Animation != RenderPipelineBenchmarkAnimation.None;
        if ((expectsAnimation && distinctOutputs < 2) || (!expectsAnimation && distinctOutputs != 1))
        {
            throw new InvalidOperationException(
                $"Benchmark scene '{_scene.Name}' output stability did not match its declared animation mode.");
        }

        _lastSetupFrame = observed[^1];
        _nextFrame = checked(plan[^1].Frame + 1);
    }

    public ulong RenderMeasuredFrame()
    {
        ThrowIfDisposed();
        RenderThread.Dispatcher.VerifyAccess();
        int frameIndex = _nextFrame++;
        ApplyFrameState(frameIndex);
        _lastMeasuredRasterization?.Dispose();
        RenderNodeRasterization rasterization = _renderer.Rasterize();
        _lastMeasuredRasterization = rasterization;
        Bitmap? bitmap = rasterization.Bitmap;
        _lastMeasuredFrameIndex = frameIndex;
        _lastMeasuredToken = bitmap is null ? 0 : SampleToken(bitmap.GetPixelSpan<ushort>());
        return _lastMeasuredToken;
    }

    public RenderPipelineBenchmarkCounterRecord CreateCounterRecord()
    {
        ThrowIfDisposed();
        RenderThread.Dispatcher.VerifyAccess();
        RenderPipelineObservedFrame setup = _lastSetupFrame
            ?? throw new InvalidOperationException("Benchmark setup verification did not complete.");
        if (_lastMeasuredFrameIndex < 0)
            throw new InvalidOperationException("Benchmark completed without a measured request.");
        RenderPipelineObservedFrame measured = Observe(
            _lastMeasuredRasterization
            ?? throw new InvalidOperationException("The final measured output was not retained for cleanup."));
        using var diagnosticSession = new DiagnosticSession(_scene);
        DiagnosticCapture diagnostics = diagnosticSession.Capture(_nextFrame);
        AssertMatchingDiagnosticOutput(setup, diagnostics.SetupOutput);
        AssertMatchingDiagnosticOutput(measured, diagnostics.MeasuredOutput);
        using var expectationSession = new DiagnosticSession(_scene);
        DiagnosticCapture expectation = expectationSession.Capture(_nextFrame);
        AssertMatchingDiagnosticOutput(diagnostics.MeasuredOutput, expectation.MeasuredOutput);

        return new RenderPipelineBenchmarkCounterRecord
        {
            SchemaVersion = 4,
            CaseName = _scene.Name,
            FusionMode = RenderPipelineBenchmarkConfig.GetFusionMode().ToString(),
            Fingerprint = Beutl.Evidence.RenderEvidenceFingerprint.TryCapture(
                GraphicsContextFactory.SharedContext,
                out string? fingerprintUnavailableReason),
            FingerprintUnavailableReason = fingerprintUnavailableReason,
            Seed = _scene.Seed,
            Width = setup.Width,
            Height = setup.Height,
            SetupWarmupFrames = RenderPipelineBenchmarkConfig.SetupWarmupFrameCount,
            Lifetime = RenderPipelineBenchmarkConfig.LifetimeContract,
            RequestShape = RenderPipelineBenchmarkConfig.RequestShapeContract,
            SemanticStageCount = _scene.SemanticStageCount,
            TopLevelDrawableCount = _scene.TopLevelDrawableCount,
            Animation = _scene.Animation.ToString(),
            Barrier = _scene.Barrier.ToString(),
            HasStaticPrefixCache = _scene.HasStaticPrefixCache,
            HasTargetDependencies = _scene.HasTargetDependencies,
            OutputSha256 = setup.Sha256,
            OutputChecksum = setup.Checksum.ToString("x16"),
            OutputBounds = setup.Bounds,
            MeasuredOutputSha256 = measured.Sha256,
            MeasuredOutputChecksum = measured.Checksum.ToString("x16"),
            MeasuredOutputBounds = measured.Bounds,
            MeasuredWidth = measured.Width,
            MeasuredHeight = measured.Height,
            ExpectedMeasuredOutputSha256 = expectation.MeasuredOutput.Sha256,
            ExpectedMeasuredOutputChecksum = expectation.MeasuredOutput.Checksum.ToString("x16"),
            ExpectedMeasuredOutputBounds = expectation.MeasuredOutput.Bounds,
            ExpectedMeasuredWidth = expectation.MeasuredOutput.Width,
            ExpectedMeasuredHeight = expectation.MeasuredOutput.Height,
            SetupLastRequestCounters = diagnostics.SetupCounters,
            MeasuredLastRequestCounters = diagnostics.MeasuredCounters,
            LastExecutionStatistics = diagnostics.LastExecutionStatistics,
            StructuralPlanCacheStatistics = diagnostics.StructuralPlanCacheStatistics,
            ProgramCacheStatistics = diagnostics.ProgramCacheStatistics,
            TargetPoolStatistics = diagnostics.TargetPoolStatistics,
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        RenderThread.Dispatcher.VerifyAccess();
        _lastMeasuredRasterization?.Dispose();
        _lastMeasuredRasterization = null;
        _lastSetupRasterization?.Dispose();
        _lastSetupRasterization = null;
        _renderer.Dispose();
        _root.Dispose();
        for (int index = _sceneResources.Count - 1; index >= 0; index--)
            _sceneResources[index].Dispose();
        _disposed = true;
    }

    private RenderPipelineObservedFrame RenderAndObserve(int frameIndex, bool retainRasterization)
    {
        ApplyFrameState(frameIndex);
        RenderNodeRasterization? rasterization = _renderer.Rasterize();
        try
        {
            RenderPipelineObservedFrame observed = Observe(rasterization);
            if (retainRasterization)
            {
                _lastSetupRasterization?.Dispose();
                _lastSetupRasterization = rasterization;
                _lastSetupFrameIndex = frameIndex;
                rasterization = null;
            }
            return observed;
        }
        finally
        {
            rasterization?.Dispose();
        }
    }

    private static RenderPipelineObservedFrame Observe(RenderNodeRasterization rasterization)
    {
        Bitmap? bitmap = rasterization.Bitmap;
        if (bitmap is null)
        {
            return new RenderPipelineObservedFrame(
                true,
                rasterization.Bounds,
                0,
                0,
                0,
                0,
                string.Empty,
                0);
        }

        Span<byte> bytes = bitmap.GetPixelSpan();
        string sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Span<ushort> components = bitmap.GetPixelSpan<ushort>();
        ulong token = SampleToken(components);
        ulong checksum = CalculateChecksum(components);
        double energy = 0;
        for (int index = 0; index < components.Length; index += 17)
            energy += Math.Abs((float)BitConverter.UInt16BitsToHalf(components[index]));

        return new RenderPipelineObservedFrame(
            false,
            rasterization.Bounds,
            bitmap.Width,
            bitmap.Height,
            token,
            checksum,
            sha256,
            energy);
    }

    private void ApplyFrameState(int frameIndex)
    {
        RenderPipelineBenchmarkFrameState state = _scene.GetFrameState(frameIndex);
        foreach (IFrameStateConsumer node in _animatedNodes)
            node.Apply(state);
    }

    private static void ValidateSceneCounters(
        RenderPipelineBenchmarkSceneDefinition scene,
        IReadOnlyDictionary<string, long> counters)
    {
        // The primary workload has to prove which shape it actually ran, because a paired SC-008 run measures
        // it in both: with fusion on it must fuse, and with fusion off it must not. Asserting only the fused
        // shape would make the workload unable to supply its own baseline, and asserting neither would let a
        // silently unfused run be reported as the feature's timing.
        FusionMode fusionMode = RenderPipelineBenchmarkConfig.GetFusionMode();
        if (scene.Name == "ShaderOpacityShader")
        {
            long fusedRuns = counters.GetValueOrDefault("FusedShaderRunExecutions");
            if (fusionMode == FusionMode.Enabled && fusedRuns < 1)
            {
                throw new InvalidOperationException(
                    "The primary workload did not execute its fused three-stage chain.");
            }

            if (fusionMode == FusionMode.Disabled && fusedRuns != 0)
            {
                throw new InvalidOperationException(
                    "The primary workload fused its three-stage chain while fusion was disabled, so it cannot "
                    + "serve as an unfused baseline.");
            }
        }

        // Built-in spatial filters may replay directly, but a whole-source shader still splits shader runs.
        // CustomEffect topology is pinned by the benchmark unit test because its own target is not leased from
        // the renderer pool and therefore cannot be inferred from IntermediateTargetAcquisitions.
        long barrierEvidence = scene.Barrier switch
        {
            RenderPipelineBenchmarkBarrier.WholeSourceShader => counters.GetValueOrDefault("ShaderRunExecutions"),
            _ => long.MaxValue,
        };
        if (barrierEvidence < 1)
        {
            throw new InvalidOperationException($"Barrier workload '{scene.Name}' did not retain a hard island boundary.");
        }

        if (scene.HasStaticPrefixCache && counters.GetValueOrDefault("StructuralPlanCacheHits") < 1)
        {
            throw new InvalidOperationException("The static-prefix workload did not reach its persistent render cache.");
        }

        if (scene.HasTargetDependencies
            && counters.GetValueOrDefault("TargetPoolCreates") < 1)
        {
            throw new InvalidOperationException("The multi-root workload did not record every target dependency.");
        }
    }

    private static void ValidateRequestCounters(IReadOnlyDictionary<string, long> counters)
    {
        if (counters.Count == 0 || !counters.ContainsKey("ShaderStageExecutions"))
            throw new InvalidOperationException("A benchmark request produced no request-wide diagnostics.");
        if (counters.GetValueOrDefault("Failures") != 0
            || counters.GetValueOrDefault("CleanupFailures") != 0
            || counters.GetValueOrDefault("FailedOutcomes") != 0)
        {
            throw new InvalidOperationException("A benchmark request reported a render or cleanup failure.");
        }
        if (counters.GetValueOrDefault("IntermediateAcquires")
            != counters.GetValueOrDefault("IntermediateDischarges"))
        {
            throw new InvalidOperationException("A benchmark request did not discharge every intermediate acquire.");
        }

        long outcomes = counters.GetValueOrDefault("ExecutedOutcomes")
                        + counters.GetValueOrDefault("CachedOutcomes")
                        + counters.GetValueOrDefault("MetadataOutcomes")
                        + counters.GetValueOrDefault("SkippedOutcomes")
                        + counters.GetValueOrDefault("FailedOutcomes");
        if (outcomes != counters.GetValueOrDefault("RecordedFragments"))
            throw new InvalidOperationException("A benchmark request did not reconcile every recorded fragment.");
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

    private static ulong SampleToken(ReadOnlySpan<ushort> components)
        => components.Length == 0
            ? 0
            : ((ulong)components[0] << 48)
              | ((ulong)components[components.Length / 3] << 32)
              | ((ulong)components[components.Length * 2 / 3] << 16)
              | components[^1];

    private static int[] GetSetupFrames(RenderPipelineBenchmarkSceneDefinition scene)
        => scene.Animation == RenderPipelineBenchmarkAnimation.StructuralToggle
            ? [0, 1, 7, 8, 9]
            : Enumerable.Range(0, RenderPipelineBenchmarkConfig.SetupWarmupFrameCount).ToArray();

    private static (int Frame, bool RetainRasterization)[] GetSetupRenderPlan(
        RenderPipelineBenchmarkSceneDefinition scene)
    {
        int[] frames = GetSetupFrames(scene);
        return frames
            .Select((frame, index) => (frame, index == frames.Length - 1))
            .ToArray();
    }

    internal static IReadOnlyList<(int Frame, bool RetainRasterization)> GetSetupRenderPlanForTest(
        string caseName)
        => GetSetupRenderPlan(RenderPipelineBenchmarkScenes.Get(caseName));

    private static RenderNodeRendererOptions CreateRendererOptions(RenderPipelineBenchmarkSceneDefinition scene)
        => new()
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                TargetDomain = s_targetDomain,
                OutputScale = 1,
                MaxWorkingScale = 1,
                CacheOptions = new Beutl.Graphics.Rendering.Cache.RenderCacheOptions(scene.HasStaticPrefixCache, Beutl.Graphics.Rendering.Cache.RenderCacheRules.Default),
                FusionMode = RenderPipelineBenchmarkConfig.GetFusionMode(),
            },
        };

    private static void AssertMatchingDiagnosticOutput(
        RenderPipelineObservedFrame production,
        RenderPipelineObservedFrame diagnostic)
    {
        if (production.IsEmpty != diagnostic.IsEmpty
            || production.Bounds != diagnostic.Bounds
            || production.Width != diagnostic.Width
            || production.Height != diagnostic.Height
            || production.Token != diagnostic.Token
            || production.Checksum != diagnostic.Checksum
            || !string.Equals(production.Sha256, diagnostic.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Independent benchmark output captures did not reproduce the same output.");
        }
    }

    private static RenderNode CreateScene(
        RenderPipelineBenchmarkSceneDefinition scene,
        List<IFrameStateConsumer> animatedNodes,
        List<IDisposable> sceneResources)
    {
        return scene.Name switch
        {
            "NoEffectControl" => CreateSource(scene, s_targetDomain),
            "SingleShader" => WrapShader(CreateSource(scene, s_targetDomain), BenchmarkShader.Gamma),
            "ShaderOpacityShader" => CreatePrimary(scene, barrier: false),
            "ShaderOpacityShaderBarrier" => CreatePrimary(scene, barrier: true),
            "LongInvariantChain" => CreateLongChain(scene),
            "ParameterOnlyAnimation" => CreateAnimatedChain(scene, animatedNodes),
            "StructuralToggle" => CreateStructuralToggle(scene, animatedNodes),
            "StaticPrefixAnimatedTail" => CreateStaticPrefix(scene, animatedNodes),
            "StaticSpatialPrefixAnimatedBlurTail" => CreateStaticSpatialPrefix(
                scene,
                animatedNodes,
                sceneResources),
            "MixedSpatialColor" => CreateMixedChain(scene, sceneResources),
            "SpatialGroupChain" => CreateSpatialGroupChain(scene, sceneResources),
            "SpatialNodeChain" => CreateSpatialNodeChain(scene, sceneResources),
            "LayerCustomEffect" => CreateCustomEffectChain(scene, sceneResources, mixed: false),
            "BlurCustomBlur" => CreateCustomEffectChain(scene, sceneResources, mixed: true),
            "SmallObjectFixedOverhead" => CreateSmallObject(scene),
            "MultipleDrawablesTargetDependencies" => CreateMultipleRoots(scene),
            _ => throw new ArgumentOutOfRangeException(nameof(scene), scene.Name, "Unknown benchmark scene."),
        };
    }

    private static RenderNode CreatePrimary(RenderPipelineBenchmarkSceneDefinition scene, bool barrier)
    {
        RenderNode current = CreateSource(scene, s_targetDomain);
        current = WrapShader(current, BenchmarkShader.Gamma);
        current = WrapOpacity(current, 0.625f);
        if (barrier)
            current = WrapShader(current, BenchmarkShader.WholeSourceIdentity);
        return WrapShader(current, BenchmarkShader.Invert);
    }

    private static RenderNode CreateLongChain(RenderPipelineBenchmarkSceneDefinition scene)
    {
        RenderNode current = CreateSource(scene, s_targetDomain);
        for (int index = 0; index < scene.SemanticStageCount; index++)
            current = WrapShader(current, (index & 1) == 0 ? BenchmarkShader.Gamma : BenchmarkShader.Invert);
        return current;
    }

    private static RenderNode CreateAnimatedChain(
        RenderPipelineBenchmarkSceneDefinition scene,
        List<IFrameStateConsumer> animatedNodes)
    {
        RenderNode current = WrapShader(CreateSource(scene, s_targetDomain), BenchmarkShader.Gamma);
        var animated = new BenchmarkAnimatedShaderNode();
        animated.AddChild(current);
        animatedNodes.Add(animated);
        return WrapShader(animated, BenchmarkShader.Invert);
    }

    private static RenderNode CreateStructuralToggle(
        RenderPipelineBenchmarkSceneDefinition scene,
        List<IFrameStateConsumer> animatedNodes)
    {
        RenderNode current = WrapShader(CreateSource(scene, s_targetDomain), BenchmarkShader.Gamma);
        var toggle = new BenchmarkStructuralToggleNode();
        toggle.AddChild(current);
        animatedNodes.Add(toggle);
        return WrapShader(toggle, BenchmarkShader.Invert);
    }

    private static RenderNode CreateStaticPrefix(
        RenderPipelineBenchmarkSceneDefinition scene,
        List<IFrameStateConsumer> animatedNodes)
    {
        RenderNode prefix = CreateSource(scene, s_targetDomain);
        prefix = WrapShader(prefix, BenchmarkShader.Gamma);
        prefix = WrapShader(prefix, BenchmarkShader.Invert);
        prefix = WrapShader(prefix, BenchmarkShader.ChannelRotate);
        var cacheBoundary = new BenchmarkCacheBoundaryNode();
        cacheBoundary.AddChild(prefix);
        for (int i = 0; i < RenderNodeCache.StableRequestCount; i++)
            cacheBoundary.Cache.RecordSuccessfulStableRequest();

        var animated = new BenchmarkAnimatedShaderNode();
        animated.AddChild(cacheBoundary);
        animatedNodes.Add(animated);
        RenderNode current = WrapOpacity(animated, 0.875f);
        return WrapShader(current, BenchmarkShader.ChannelRotate);
    }

    private static RenderNode CreateStaticSpatialPrefix(
        RenderPipelineBenchmarkSceneDefinition scene,
        List<IFrameStateConsumer> animatedNodes,
        List<IDisposable> sceneResources)
    {
        FilterEffect.Resource prefixResource = new Blur
        {
            Sigma = { CurrentValue = new Size(3, 3) },
        }.ToResource(CompositionContext.Default);
        sceneResources.Add(prefixResource);
        FilterEffectRenderNode prefix = prefixResource.CreateRenderNode();
        prefix.AddChild(CreateSource(scene, s_targetDomain));

        var cacheBoundary = new BenchmarkCacheBoundaryNode();
        cacheBoundary.AddChild(prefix);
        for (int i = 0; i < RenderNodeCache.StableRequestCount; i++)
            cacheBoundary.Cache.RecordSuccessfulStableRequest();

        var tailEffect = new Blur();
        FilterEffect.Resource tailResource = tailEffect.ToResource(CompositionContext.Default);
        sceneResources.Add(tailResource);
        FilterEffectRenderNode tail = tailResource.CreateRenderNode();
        tail.AddChild(cacheBoundary);
        animatedNodes.Add(new BenchmarkAnimatedBlurNode(tailEffect, tailResource, tail));
        return tail;
    }

    private static RenderNode CreateMixedChain(
        RenderPipelineBenchmarkSceneDefinition scene,
        List<IDisposable> sceneResources)
    {
        RenderNode current = WrapShader(CreateSource(scene, s_targetDomain), BenchmarkShader.Gamma);
        Blur blur = CreateMixedSpatialEffect();
        FilterEffect.Resource blurResource = blur.ToResource(CompositionContext.Default);
        sceneResources.Add(blurResource);
        FilterEffectRenderNode blurNode = blurResource.CreateRenderNode();
        blurNode.AddChild(current);
        current = blurNode;
        current = WrapShader(current, BenchmarkShader.Invert);
        current = WrapOpacity(current, 0.8f);
        return WrapShader(current, BenchmarkShader.ChannelRotate);
    }

    // One effect node whose group holds every blur: the recorder keeps them in one segment.
    private static RenderNode CreateSpatialGroupChain(
        RenderPipelineBenchmarkSceneDefinition scene,
        List<IDisposable> sceneResources)
    {
        var group = new FilterEffectGroup();
        for (int index = 0; index < scene.SemanticStageCount; index++)
            group.Children.Add(new Blur { Sigma = { CurrentValue = new Size(2 + index, 2 + index) } });

        FilterEffect.Resource resource = group.ToResource(CompositionContext.Default);
        sceneResources.Add(resource);
        FilterEffectRenderNode node = resource.CreateRenderNode();
        node.AddChild(CreateSource(scene, s_targetDomain));
        return node;
    }

    // One effect node per blur: each node records its own segment.
    private static RenderNode CreateSpatialNodeChain(
        RenderPipelineBenchmarkSceneDefinition scene,
        List<IDisposable> sceneResources)
    {
        RenderNode current = CreateSource(scene, s_targetDomain);
        for (int index = 0; index < scene.SemanticStageCount; index++)
        {
            var blur = new Blur { Sigma = { CurrentValue = new Size(2 + index, 2 + index) } };
            FilterEffect.Resource resource = blur.ToResource(CompositionContext.Default);
            sceneResources.Add(resource);
            FilterEffectRenderNode node = resource.CreateRenderNode();
            node.AddChild(current);
            current = node;
        }

        return current;
    }

    private static RenderNode CreateCustomEffectChain(
        RenderPipelineBenchmarkSceneDefinition scene,
        List<IDisposable> sceneResources,
        bool mixed)
    {
        FilterEffect effect = CreateCustomEffect(mixed);
        FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        sceneResources.Add(resource);
        FilterEffectRenderNode node = resource.CreateRenderNode();
        node.AddChild(CreateSource(scene, s_targetDomain));
        return node;
    }

    private static FilterEffect CreateCustomEffect(bool mixed)
    {
        var group = new FilterEffectGroup();
        if (mixed)
            group.Children.Add(new Blur { Sigma = { CurrentValue = new Size(3, 3) } });
        group.Children.Add(new LayerEffect());
        if (mixed)
            group.Children.Add(new Blur { Sigma = { CurrentValue = new Size(4, 4) } });
        return group;
    }

    internal static FilterEffect CreateCustomEffectForTest(bool mixed)
        => CreateCustomEffect(mixed);

    private static Blur CreateMixedSpatialEffect()
    {
        var blur = new Blur();
        blur.Sigma.CurrentValue = new Size(3, 3);
        return blur;
    }

    internal static FilterEffect CreateMixedSpatialEffectForTest()
        => CreateMixedSpatialEffect();

    private static RenderNode CreateSmallObject(RenderPipelineBenchmarkSceneDefinition scene)
    {
        Rect bounds = RenderPipelineBenchmarkScenes.GetDrawableBounds(scene, 0);
        RenderNode current = WrapShader(CreateSource(scene, bounds), BenchmarkShader.Gamma);
        current = WrapOpacity(current, 0.75f);
        return WrapShader(current, BenchmarkShader.Invert);
    }

    private static RenderNode CreateMultipleRoots(RenderPipelineBenchmarkSceneDefinition scene)
    {
        var root = new ContainerRenderNode();
        for (int index = 0; index < scene.TopLevelDrawableCount; index++)
        {
            Rect bounds = RenderPipelineBenchmarkScenes.GetDrawableBounds(scene, index);
            RenderNode source = WrapShader(CreateSource(scene, bounds, index), BenchmarkShader.ChannelRotate);
            var dependency = new BenchmarkTargetDependencyNode(bounds, index);
            dependency.AddChild(source);
            root.AddChild(dependency);
        }
        return root;
    }

    private static RenderNode CreateSource(
        RenderPipelineBenchmarkSceneDefinition scene,
        Rect bounds,
        int variant = 0)
    {
        var size = new PixelSize((int)bounds.Width, (int)bounds.Height);
        RenderTarget target = RenderTarget.Create(size.Width, size.Height)
            ?? throw new InvalidOperationException(
                $"Could not allocate the persistent {size.Width}x{size.Height} benchmark source.");
        using (var bitmap = new Bitmap(
                   size.Width,
                   size.Height,
                   BitmapColorType.RgbaF16,
                   BitmapAlphaType.Premul,
                   BitmapColorSpace.LinearSrgb))
        {
            RenderPipelineBenchmarkSceneDefinition sourceScene = variant == 0
                ? scene
                : new RenderPipelineBenchmarkSceneDefinition(
                    scene.Name + "-source-" + variant,
                    scene.Seed + variant * 101,
                    scene.SemanticStageCount);
            RenderPipelineBenchmarkScenes.CreateLinearPremultipliedRgba16F(sourceScene, size)
                .CopyTo(bitmap.GetPixelSpan<Half>());
            using var canvas = new ImmediateCanvas(target, RenderIntent.Preview, 1, 1, new Size(size.Width, size.Height));
            canvas.Clear();
            canvas.DrawBitmap(bitmap, Brushes.Resource.White, null);
        }
        return new BenchmarkMaterializedSourceNode(target, bounds);
    }

    private static RenderNode WrapShader(RenderNode child, ShaderDescription description)
    {
        var node = new BenchmarkShaderNode(description);
        node.AddChild(child);
        return node;
    }

    private static RenderNode WrapOpacity(RenderNode child, float opacity)
    {
        var node = new OpacityRenderNode(opacity);
        node.AddChild(child);
        return node;
    }

    private sealed class DiagnosticSession : IDisposable
    {
        private readonly RenderPipelineBenchmarkSceneDefinition _scene;
        private readonly RenderNode _root;
        private readonly RenderNodeRenderer _renderer;
        private readonly IReadOnlyList<IFrameStateConsumer> _animatedNodes;
        private readonly IReadOnlyList<IDisposable> _sceneResources;
        private bool _disposed;

        public DiagnosticSession(RenderPipelineBenchmarkSceneDefinition scene)
        {
            _scene = scene;
            var animatedNodes = new List<IFrameStateConsumer>();
            var sceneResources = new List<IDisposable>();
            _root = CreateScene(scene, animatedNodes, sceneResources);
            _animatedNodes = animatedNodes.AsReadOnly();
            _sceneResources = sceneResources.AsReadOnly();
            RenderNodeRendererOptions options = CreateRendererOptions(scene);
            RenderPipelineInternalDiagnostics.Attach(options, RenderRequestPurpose.Frame);
            _renderer = new RenderNodeRenderer(_root, options);
        }

        public DiagnosticCapture Capture(int productionNextFrame)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            int[] setupFrames = GetSetupFrames(_scene);
            RenderPipelineObservedFrame? setupOutput = null;
            SortedDictionary<string, long>? setupCounters = null;
            for (int index = 0; index < setupFrames.Length; index++)
            {
                ApplyFrameState(setupFrames[index]);
                using RenderNodeRasterization rasterization = _renderer.Rasterize();
                SortedDictionary<string, long> counters = CaptureCounters(setupFrames[index], "setup");
                ValidateRequestCounters(counters);
                if (index == setupFrames.Length - 1)
                {
                    setupOutput = Observe(rasterization);
                    setupCounters = counters;
                }
            }

            SortedDictionary<string, long> verifiedSetupCounters = setupCounters
                ?? throw new InvalidOperationException("The untimed diagnostic setup did not render a request.");
            ValidateSceneCounters(_scene, verifiedSetupCounters);

            int firstMeasuredFrame = checked(setupFrames[^1] + 1);
            if (productionNextFrame <= firstMeasuredFrame)
                throw new InvalidOperationException("The production benchmark completed without a measured request.");

            SortedDictionary<string, long>? measuredCounters = null;
            RenderPipelineObservedFrame? measuredOutput = null;
            for (int frameIndex = firstMeasuredFrame; frameIndex < productionNextFrame; frameIndex++)
            {
                ApplyFrameState(frameIndex);
                using RenderNodeRasterization rasterization = _renderer.Rasterize();
                SortedDictionary<string, long> counters = CaptureCounters(frameIndex, "measured-shape");
                ValidateRequestCounters(counters);
                if (frameIndex == productionNextFrame - 1)
                {
                    measuredOutput = Observe(rasterization);
                    measuredCounters = counters;
                }
            }
            SortedDictionary<string, long> verifiedMeasuredCounters = measuredCounters
                ?? throw new InvalidOperationException(
                    "The untimed diagnostic session did not replay the final measured request.");
            ValidateSceneCounters(_scene, verifiedMeasuredCounters);

            return new DiagnosticCapture(
                setupOutput
                    ?? throw new InvalidOperationException("The untimed diagnostic setup produced no output."),
                measuredOutput
                    ?? throw new InvalidOperationException("The untimed diagnostic session produced no measured output."),
                verifiedSetupCounters,
                verifiedMeasuredCounters,
                RenderPipelineInternalDiagnostics.CaptureNumericProperties(
                    _renderer,
                    "LastExecutionStatistics"),
                RenderPipelineInternalDiagnostics.CaptureNumericProperties(
                    _renderer,
                    "StructuralPlanCacheStatistics"),
                RenderPipelineInternalDiagnostics.CaptureNumericProperties(
                    _renderer,
                    "ProgramCacheStatistics"),
                RenderPipelineInternalDiagnostics.CaptureNumericProperties(
                    _renderer,
                    "TargetPoolStatistics"));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _renderer.Dispose();
            _root.Dispose();
            for (int index = _sceneResources.Count - 1; index >= 0; index--)
                _sceneResources[index].Dispose();
            _disposed = true;
        }

        private SortedDictionary<string, long> CaptureCounters(int frameIndex, string phase)
        {
            SortedDictionary<string, long> counters =
                RenderPipelineInternalDiagnostics.CaptureLatestCounters(_renderer, out bool succeeded);
            if (!succeeded)
            {
                throw new InvalidOperationException(
                    $"Untimed {phase} diagnostic render {frameIndex} for '{_scene.Name}' failed.");
            }
            return counters;
        }

        private void ApplyFrameState(int frameIndex)
        {
            RenderPipelineBenchmarkFrameState state = _scene.GetFrameState(frameIndex);
            foreach (IFrameStateConsumer node in _animatedNodes)
                node.Apply(state);
        }
    }

    private sealed record DiagnosticCapture(
        RenderPipelineObservedFrame SetupOutput,
        RenderPipelineObservedFrame MeasuredOutput,
        SortedDictionary<string, long> SetupCounters,
        SortedDictionary<string, long> MeasuredCounters,
        SortedDictionary<string, long> LastExecutionStatistics,
        SortedDictionary<string, long> StructuralPlanCacheStatistics,
        SortedDictionary<string, long> ProgramCacheStatistics,
        SortedDictionary<string, long> TargetPoolStatistics);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

internal interface IFrameStateConsumer
{
    void Apply(RenderPipelineBenchmarkFrameState state);
}

internal sealed class BenchmarkMaterializedSourceNode(
    RenderTarget target,
    Rect bounds) : RenderNode
{
    public override void Process(RenderNodeContext context)
    {
        RenderResource<RenderTarget> resource = context.Borrow(target);
        context.Publish(context.MaterializedInput(MaterializedInputDescription.FromRenderTarget(
            resource,
            bounds,
            EffectiveScale.At(1),
            PixelRect.FromRect(bounds, 1),
            default,
            RenderHitTestContract.OutputBounds)));
    }

    protected override void OnDispose(bool disposing)
    {
        if (disposing)
            target.Dispose();
    }
}

internal sealed class BenchmarkShaderNode(ShaderDescription description) : ContainerRenderNode
{
    public override void Process(RenderNodeContext context)
    {
        foreach (RenderFragmentHandle input in context.Inputs)
            context.Publish(context.Shader(input, description));
    }
}

internal sealed class BenchmarkAnimatedShaderNode : ContainerRenderNode, IFrameStateConsumer
{
    private float _amount = 1;

    public void Apply(RenderPipelineBenchmarkFrameState state)
    {
        if (_amount.Equals(state.AnimatedAmount))
            return;

        _amount = state.AnimatedAmount;
        MarkChanged();
    }

    public override void Process(RenderNodeContext context)
    {
        float amount = _amount;
        ShaderDescription description = ShaderDescription.CurrentPixel(
            "uniform float amount; half4 apply(half4 color) { "
            + "return half4(min(color.rgb * amount, color.aaa), color.a); }",
            bindings => bindings.Uniform("amount", amount));
        foreach (RenderFragmentHandle input in context.Inputs)
            context.Publish(context.Shader(input, description));
    }
}

internal sealed class BenchmarkAnimatedBlurNode(
    Blur effect,
    FilterEffect.Resource resource,
    FilterEffectRenderNode node) : IFrameStateConsumer
{
    private float _sigma;

    public void Apply(RenderPipelineBenchmarkFrameState state)
    {
        float sigma = 1 + ((state.AnimatedAmount - 0.75f) * 8);
        if (_sigma.Equals(sigma))
            return;

        _sigma = sigma;
        effect.Sigma.CurrentValue = new Size(sigma, sigma);
        bool updateOnly = false;
        resource.Update(effect, CompositionContext.Default, ref updateOnly);
        if (!node.Update(resource))
            throw new InvalidOperationException("The animated Blur resource did not publish its changed sigma.");
    }
}

internal sealed class BenchmarkStructuralToggleNode : ContainerRenderNode, IFrameStateConsumer
{
    private bool _variant;

    public void Apply(RenderPipelineBenchmarkFrameState state)
    {
        if (_variant == state.StructuralVariant)
            return;

        _variant = state.StructuralVariant;

        // Without this the recording cache reuses the previous shader description and the declared structural
        // change never reaches the output, which the scene's own animation-mode check then rejects.
        MarkChanged();
    }

    public override void Process(RenderNodeContext context)
    {
        ShaderDescription description = _variant ? BenchmarkShader.ChannelRotate : BenchmarkShader.Invert;
        foreach (RenderFragmentHandle input in context.Inputs)
            context.Publish(context.Shader(input, description));
    }
}

internal sealed class BenchmarkCacheBoundaryNode : ContainerRenderNode
{
    public override void Process(RenderNodeContext context) => context.PassThrough();
}

internal sealed class BenchmarkTargetDependencyNode(Rect bounds, int index) : ContainerRenderNode
{
    public override void Process(RenderNodeContext context)
    {
        context.PublishRange(context.Inputs);
        TargetCommandDescription command = TargetCommandDescription.Create(
            index,
            static (_, _) => { },
            TargetRegion.Region(bounds),
            bounds,
            RenderHitTestContract.OutputBounds);
        context.Publish(context.TargetCommand(context.Inputs, command));
    }
}

internal static class BenchmarkShader
{
    public static ShaderDescription Gamma { get; } = ShaderDescription.CurrentPixel(
        "half4 apply(half4 color) { return half4(sqrt(max(color.rgb, half3(0))), color.a); }");

    public static ShaderDescription Invert { get; } = ShaderDescription.CurrentPixel(
        "half4 apply(half4 color) { return half4(color.a - color.rgb, color.a); }");

    public static ShaderDescription ChannelRotate { get; } = ShaderDescription.CurrentPixel(
        "half4 apply(half4 color) { return half4(color.g, color.b, color.r, color.a); }");

    public static ShaderDescription WholeSourceIdentity { get; } = ShaderDescription.WholeSource(
        "uniform shader src; half4 main(float2 coord) { return src.eval(coord); }",
        RenderBoundsContract.Identity);
}

internal sealed record RenderPipelineObservedFrame(
    bool IsEmpty,
    Rect Bounds,
    int Width,
    int Height,
    ulong Token,
    ulong Checksum,
    string Sha256,
    double Energy);

internal static class RenderPipelineInternalDiagnostics
{
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // There is no request-wide diagnostics recorder, so counters come from the component statistics
    // the renderer publishes rather than from a per-request snapshot.
    public static void Attach(RenderNodeRendererOptions options, RenderRequestPurpose purpose)
        => SetPurpose(options, purpose);

    public static void SetPurpose(RenderNodeRendererOptions options, RenderRequestPurpose purpose)
        => SetProperty(GetProperty(options, "DefaultRequest"), "Purpose", purpose);

    public static SortedDictionary<string, long> CaptureLatestCounters(object renderer, out bool succeeded)
    {
        SortedDictionary<string, long> result = CaptureNumericProperties(renderer, "LastExecutionStatistics");
        foreach (KeyValuePair<string, long> pair in CaptureNumericProperties(renderer, "TargetPoolStatistics"))
            result[$"TargetPool{pair.Key}"] = pair.Value;
        foreach (KeyValuePair<string, long> pair in CaptureNumericProperties(renderer, "ProgramCacheStatistics"))
            result[$"ProgramCache{pair.Key}"] = pair.Value;
        foreach (KeyValuePair<string, long> pair in CaptureNumericProperties(renderer, "StructuralPlanCacheStatistics"))
            result[$"StructuralPlanCache{pair.Key}"] = pair.Value;
        succeeded = true;
        return result;
    }

    public static SortedDictionary<string, long> CaptureNumericProperties(object owner, string propertyName)
    {
        object value = GetProperty(owner, propertyName);
        var result = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (PropertyInfo property in value.GetType().GetProperties(InstanceFlags).OrderBy(static x => x.Name))
        {
            object? propertyValue = property.GetValue(value);
            if (TryConvertInt64(propertyValue, out long number))
                result.Add(property.Name, number);
        }
        return result;
    }

    private static bool TryConvertInt64(object? value, out long result)
    {
        switch (value)
        {
            case byte item: result = item; return true;
            case sbyte item: result = item; return true;
            case short item: result = item; return true;
            case ushort item: result = item; return true;
            case int item: result = item; return true;
            case uint item: result = item; return true;
            case long item: result = item; return true;
            case ulong item when item <= long.MaxValue: result = (long)item; return true;
            default: result = 0; return false;
        }
    }

    private static object GetProperty(object owner, string name)
    {
        PropertyInfo property = owner.GetType().GetProperty(name, InstanceFlags)
            ?? throw new MissingMemberException(owner.GetType().FullName, name);
        return property.GetValue(owner)
            ?? throw new InvalidOperationException($"Property '{name}' unexpectedly returned null.");
    }

    private static void SetProperty(object owner, string name, object value)
    {
        PropertyInfo property = owner.GetType().GetProperty(name, InstanceFlags)
            ?? throw new MissingMemberException(owner.GetType().FullName, name);
        property.SetValue(owner, value);
    }
}

internal sealed class RenderPipelineBenchmarkCounterRecord
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public int SchemaVersion { get; init; }
    public string CaseName { get; init; } = string.Empty;

    /// <summary>Which side of a paired run produced this record.</summary>
    public string FusionMode { get; init; } = string.Empty;

    /// <summary>The machine, device, driver and build identity this run was measured on.</summary>
    /// <remarks>
    /// A paired analysis refuses to accept two runs whose comparability keys differ, so this is the field that
    /// stops drift between machines from being read as an effect. It is nullable because a benchmark must not
    /// fail for want of a fingerprint; the analyzer then reports the run as not comparable.
    /// </remarks>
    public Beutl.Evidence.RenderEvidenceFingerprint? Fingerprint { get; init; }

    public string? FingerprintUnavailableReason { get; init; }

    public int Seed { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int SetupWarmupFrames { get; init; }
    public string Lifetime { get; init; } = string.Empty;
    public string RequestShape { get; init; } = string.Empty;
    public int SemanticStageCount { get; init; }
    public int TopLevelDrawableCount { get; init; }
    public string Animation { get; init; } = string.Empty;
    public string Barrier { get; init; } = string.Empty;
    public bool HasStaticPrefixCache { get; init; }
    public bool HasTargetDependencies { get; init; }
    public string OutputSha256 { get; init; } = string.Empty;
    public string OutputChecksum { get; init; } = string.Empty;
    public Rect OutputBounds { get; init; }
    public string MeasuredOutputSha256 { get; init; } = string.Empty;
    public string MeasuredOutputChecksum { get; init; } = string.Empty;
    public Rect MeasuredOutputBounds { get; init; }
    public int MeasuredWidth { get; init; }
    public int MeasuredHeight { get; init; }
    public string ExpectedMeasuredOutputSha256 { get; init; } = string.Empty;
    public string ExpectedMeasuredOutputChecksum { get; init; } = string.Empty;
    public Rect ExpectedMeasuredOutputBounds { get; init; }
    public int ExpectedMeasuredWidth { get; init; }
    public int ExpectedMeasuredHeight { get; init; }
    public SortedDictionary<string, long> SetupLastRequestCounters { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, long> MeasuredLastRequestCounters { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, long> LastExecutionStatistics { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, long> StructuralPlanCacheStatistics { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, long> ProgramCacheStatistics { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, long> TargetPoolStatistics { get; init; } = new(StringComparer.Ordinal);
}
