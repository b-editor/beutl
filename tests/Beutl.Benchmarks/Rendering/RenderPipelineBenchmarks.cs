using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using BenchmarkDotNet.Attributes;

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
    private static readonly Rect s_targetDomain = new(
        0,
        0,
        RenderPipelineBenchmarkScenes.ReferenceSize.Width,
        RenderPipelineBenchmarkScenes.ReferenceSize.Height);

    private readonly RenderPipelineBenchmarkSceneDefinition _scene;
    private readonly RenderNode _root;
    private readonly RenderNodeRenderer _renderer;
    private readonly IReadOnlyList<IFrameStateConsumer> _animatedNodes;
    private readonly RenderPipelineEvidenceFingerprint _fingerprint;
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
        IGraphicsContext graphicsContext = GraphicsContextFactory.GetOrCreateShared()
            ?? throw new InvalidOperationException(
                "A real graphics context is required for render-pipeline benchmarks.");
        _fingerprint = RenderPipelineEvidenceFingerprint.Capture(graphicsContext);

        var animatedNodes = new List<IFrameStateConsumer>();
        _root = CreateScene(_scene, animatedNodes);
        _animatedNodes = animatedNodes.AsReadOnly();
        RenderNodeRendererOptions options = CreateRendererOptions(_scene);
        RenderPipelineInternalDiagnostics.SetPurpose(options, RenderRequestPurpose.Frame);
        _renderer = new RenderNodeRenderer(_root, options);
    }

    public void WarmAndVerify()
    {
        ThrowIfDisposed();
        RenderThread.Dispatcher.VerifyAccess();

        int[] frames = GetSetupFrames(_scene);
        var observed = new List<RenderPipelineObservedFrame>(frames.Length);
        foreach (int frame in frames)
            observed.Add(RenderAndObserve(frame));

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
        _nextFrame = checked(frames[^1] + 1);

        // Retain the final setup frame's rasterization so its RGBA16F payload can be
        // archived for cross-pipeline visual comparison without re-rendering.
        _lastSetupRasterization?.Dispose();
        ApplyFrameState(frames[^1]);
        _lastSetupRasterization = _renderer.Rasterize();
        _lastSetupFrameIndex = frames[^1];
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

        string? outputBlobsPath = RenderPipelineBenchmarkConfig.GetOutputBlobsPath();
        string setupBlobFile = string.Empty;
        if (outputBlobsPath is not null && _lastSetupRasterization?.Bitmap is { } setupBitmap)
        {
            setupBlobFile = _scene.Name + ".rgba16f";
            byte[] payload = Rgba16fEvidenceWriter.Encode(setupBitmap);
            Directory.CreateDirectory(outputBlobsPath);
            File.WriteAllBytes(Path.Combine(outputBlobsPath, setupBlobFile), payload);
        }

        return new RenderPipelineBenchmarkCounterRecord
        {
            SchemaVersion = 2,
            CaseName = _scene.Name,
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
            SetupOutputBlobFile = setupBlobFile,
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
            Fingerprint = _fingerprint,
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
        _disposed = true;
    }

    private RenderPipelineObservedFrame RenderAndObserve(int frameIndex)
    {
        ApplyFrameState(frameIndex);
        using RenderNodeRasterization rasterization = _renderer.Rasterize();
        return Observe(rasterization);
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
        if (scene.Name == "ShaderOpacityShader"
            && counters.GetValueOrDefault("FusedStages") < 3)
        {
            throw new InvalidOperationException("The primary workload did not execute its fused three-stage chain.");
        }

        if (scene.Barrier is RenderPipelineBenchmarkBarrier.WholeSourceShader
            or RenderPipelineBenchmarkBarrier.SpatialEffect
            && counters.GetValueOrDefault("ExecutionIslands") < 2)
        {
            throw new InvalidOperationException($"Barrier workload '{scene.Name}' did not retain a hard island boundary.");
        }

        if (scene.HasStaticPrefixCache && counters.GetValueOrDefault("RenderCacheHits") < 1)
        {
            throw new InvalidOperationException("The static-prefix workload did not reach its persistent render cache.");
        }

        if (scene.HasTargetDependencies
            && counters.GetValueOrDefault("RecordedTargetCommands") < scene.TopLevelDrawableCount)
        {
            throw new InvalidOperationException("The multi-root workload did not record every target dependency.");
        }
    }

    private static void ValidateRequestCounters(IReadOnlyDictionary<string, long> counters)
    {
        if (counters.Count == 0 || counters.GetValueOrDefault("RecordedFragments") <= 0)
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
        List<IFrameStateConsumer> animatedNodes)
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
            "MixedSpatialColor" => CreateMixedChain(scene),
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
        cacheBoundary.Cache.ReportRenderCount(RenderNodeCache.Count);

        var animated = new BenchmarkAnimatedShaderNode();
        animated.AddChild(cacheBoundary);
        animatedNodes.Add(animated);
        RenderNode current = WrapOpacity(animated, 0.875f);
        return WrapShader(current, BenchmarkShader.ChannelRotate);
    }

    private static RenderNode CreateMixedChain(RenderPipelineBenchmarkSceneDefinition scene)
    {
        RenderNode current = WrapShader(CreateSource(scene, s_targetDomain), BenchmarkShader.Gamma);
        current = WrapShader(current, BenchmarkShader.WholeSourceIdentity);
        current = WrapShader(current, BenchmarkShader.Invert);
        current = WrapOpacity(current, 0.8f);
        return WrapShader(current, BenchmarkShader.ChannelRotate);
    }

    private static RenderNode CreateSmallObject(RenderPipelineBenchmarkSceneDefinition scene)
    {
        int width = Math.Max(1, (int)MathF.Round(s_targetDomain.Width * scene.ContentScale));
        int height = Math.Max(1, (int)MathF.Round(s_targetDomain.Height * scene.ContentScale));
        var bounds = new Rect(
            MathF.Floor((float)(s_targetDomain.Width - width) / 2),
            MathF.Floor((float)(s_targetDomain.Height - height) / 2),
            width,
            height);
        RenderNode current = WrapShader(CreateSource(scene, bounds), BenchmarkShader.Gamma);
        current = WrapOpacity(current, 0.75f);
        return WrapShader(current, BenchmarkShader.Invert);
    }

    private static RenderNode CreateMultipleRoots(RenderPipelineBenchmarkSceneDefinition scene)
    {
        var root = new ContainerRenderNode();
        const int margin = 12;
        int width = (RenderPipelineBenchmarkScenes.ReferenceSize.Width - (margin * 3)) / 2;
        int height = (RenderPipelineBenchmarkScenes.ReferenceSize.Height - (margin * 3)) / 2;
        for (int index = 0; index < scene.TopLevelDrawableCount; index++)
        {
            int column = index & 1;
            int row = index >> 1;
            var bounds = new Rect(
                margin + (column * (width + margin)),
                margin + (row * (height + margin)),
                width,
                height);
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
            using var canvas = new ImmediateCanvas(target, 1, 1, new Size(size.Width, size.Height));
            canvas.Clear();
            canvas.DrawBitmap(bitmap, Brushes.Resource.White, null);
        }
        return new BenchmarkMaterializedSourceNode(target, bounds, scene.Name + "-source-" + variant);
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
        private readonly object _diagnostics;
        private readonly IReadOnlyList<IFrameStateConsumer> _animatedNodes;
        private bool _disposed;

        public DiagnosticSession(RenderPipelineBenchmarkSceneDefinition scene)
        {
            _scene = scene;
            var animatedNodes = new List<IFrameStateConsumer>();
            _root = CreateScene(scene, animatedNodes);
            _animatedNodes = animatedNodes.AsReadOnly();
            _diagnostics = RenderPipelineInternalDiagnostics.CreateState();
            RenderNodeRendererOptions options = CreateRendererOptions(scene);
            RenderPipelineInternalDiagnostics.Attach(options, _diagnostics, RenderRequestPurpose.Frame);
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
            _disposed = true;
        }

        private SortedDictionary<string, long> CaptureCounters(int frameIndex, string phase)
        {
            SortedDictionary<string, long> counters =
                RenderPipelineInternalDiagnostics.CaptureLatestCounters(_diagnostics, out bool succeeded);
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
    Rect bounds,
    string cacheIdentity) : RenderNode
{
    public override void Process(RenderNodeContext context)
    {
        RenderResource<RenderTarget> resource = context.Borrow(target, cacheIdentity, version: 1);
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
        _amount = state.AnimatedAmount;
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

internal sealed class BenchmarkStructuralToggleNode : ContainerRenderNode, IFrameStateConsumer
{
    private bool _variant;

    public void Apply(RenderPipelineBenchmarkFrameState state)
    {
        _variant = state.StructuralVariant;
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
            static _ => { },
            TargetRegion.Region(bounds),
            bounds,
            RenderHitTestContract.OutputBounds,
            TargetAccess.ReadWrite,
            structuralKey: $"render-pipeline-target-dependency-{index}",
            runtimeIdentity: new RenderRuntimeIdentity(index));
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

    public static object CreateState()
    {
        Type type = typeof(RenderNode).Assembly.GetType(
            "Beutl.Graphics.Rendering.RenderPipelineDiagnosticsState",
            throwOnError: true)!;
        return Activator.CreateInstance(type, nonPublic: true)
            ?? throw new InvalidOperationException("Could not create the internal render diagnostics state.");
    }

    public static void Attach(
        RenderNodeRendererOptions options,
        object state,
        RenderRequestPurpose purpose)
    {
        // Diagnostics and Purpose live on the default request record; its init-only
        // accessors remain reflection-settable before the renderer copies the request.
        object request = GetProperty(options, "DefaultRequest");
        SetProperty(request, "Diagnostics", state);
        SetProperty(request, "Purpose", purpose);
    }

    public static void SetPurpose(RenderNodeRendererOptions options, RenderRequestPurpose purpose)
        => SetProperty(GetProperty(options, "DefaultRequest"), "Purpose", purpose);

    public static SortedDictionary<string, long> CaptureLatestCounters(object state, out bool succeeded)
    {
        object snapshot = GetProperty(state, "Latest");
        succeeded = (bool)GetProperty(snapshot, "Succeeded");
        object counters = GetProperty(snapshot, "Counters");
        var result = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (object item in (IEnumerable)counters)
        {
            object key = GetProperty(item, "Key");
            object value = GetProperty(item, "Value");
            result.Add(key.ToString()!, Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
        }
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
    public string SetupOutputBlobFile { get; init; } = string.Empty;
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
    public RenderPipelineEvidenceFingerprint Fingerprint { get; init; } = new();
    public SortedDictionary<string, long> SetupLastRequestCounters { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, long> MeasuredLastRequestCounters { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, long> LastExecutionStatistics { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, long> StructuralPlanCacheStatistics { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, long> ProgramCacheStatistics { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, long> TargetPoolStatistics { get; init; } = new(StringComparer.Ordinal);
}

internal sealed class RenderPipelineEvidenceFingerprint
{
    public string OsDescription { get; init; } = string.Empty;
    public string OsVersion { get; init; } = string.Empty;
    public string OsBuild { get; init; } = string.Empty;
    public string OsArchitecture { get; init; } = string.Empty;
    public string ProcessArchitecture { get; init; } = string.Empty;
    public string RuntimeIdentifier { get; init; } = string.Empty;
    public string FrameworkDescription { get; init; } = string.Empty;
    public string EnvironmentVersion { get; init; } = string.Empty;
    public string RendererBackend { get; init; } = string.Empty;
    public string SkiaBackend { get; init; } = string.Empty;
    public string DeviceSelection { get; init; } = string.Empty;
    public string VulkanApiVersion { get; init; } = string.Empty;
    public string VulkanVendorId { get; init; } = string.Empty;
    public string VulkanDeviceId { get; init; } = string.Empty;
    public string VulkanDeviceType { get; init; } = string.Empty;
    public string VulkanDeviceName { get; init; } = string.Empty;
    public string VulkanDeviceUuid { get; init; } = string.Empty;
    public string VulkanDriverUuid { get; init; } = string.Empty;
    public string VulkanDriverId { get; init; } = string.Empty;
    public string VulkanDriverName { get; init; } = string.Empty;
    public string VulkanDriverInfo { get; init; } = string.Empty;
    public string VulkanDriverVersionRaw { get; init; } = string.Empty;
    public string VulkanDriverVersionDecoded { get; init; } = string.Empty;
    public string[] VulkanEnabledExtensions { get; init; } = [];
    public string MetalDeviceName { get; init; } = string.Empty;
    public string MetalRegistryId { get; init; } = string.Empty;
    public string MetalFeatureFamily { get; init; } = string.Empty;
    public string MetalDriver { get; init; } = string.Empty;
    public string SkiaSharpManagedVersion { get; init; } = string.Empty;
    public string SkiaSharpNativeVersion { get; init; } = string.Empty;
    public string SilkNetVulkanVersion { get; init; } = string.Empty;
    public string BeutlEngineAssemblyVersion { get; init; } = string.Empty;

    public static unsafe RenderPipelineEvidenceFingerprint Capture(IGraphicsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        PropertyInfo instanceProperty = typeof(GraphicsContextFactory).GetProperty("VulkanInstance", flags)
            ?? throw new MissingMemberException(typeof(GraphicsContextFactory).FullName, "VulkanInstance");
        object instance = instanceProperty.GetValue(null)
            ?? throw new InvalidOperationException("The Vulkan instance was not initialized.");
        Vk vk = (Vk)(instance.GetType().GetProperty("Vk", flags)?.GetValue(instance)
            ?? throw new InvalidOperationException("The Vulkan API handle was unavailable."));

        MethodInfo selectedMethod = typeof(GraphicsContextFactory).GetMethod("GetSelectedGpuDetails", flags)
            ?? throw new MissingMethodException(typeof(GraphicsContextFactory).FullName, "GetSelectedGpuDetails");
        object selected = selectedMethod.Invoke(null, null)
            ?? throw new InvalidOperationException("The selected Vulkan physical device was unavailable.");
        PhysicalDevice physicalDevice = (PhysicalDevice)(selected.GetType().GetProperty("Device", flags)?.GetValue(selected)
            ?? throw new InvalidOperationException("The selected Vulkan device handle was unavailable."));

        var idProperties = new PhysicalDeviceIDProperties
        {
            SType = StructureType.PhysicalDeviceIDProperties,
        };
        var driverProperties = new PhysicalDeviceDriverProperties
        {
            SType = StructureType.PhysicalDeviceDriverProperties,
            PNext = &idProperties,
        };
        var properties2 = new PhysicalDeviceProperties2
        {
            SType = StructureType.PhysicalDeviceProperties2,
            PNext = &driverProperties,
        };
        vk.GetPhysicalDeviceProperties2(physicalDevice, &properties2);

        PhysicalDeviceProperties properties = properties2.Properties;
        string deviceName = FixedUtf8(properties.DeviceName, Vk.MaxPhysicalDeviceNameSize);
        string driverName = FixedUtf8(driverProperties.DriverName, Vk.MaxDriverNameSize);
        string driverInfo = FixedUtf8(driverProperties.DriverInfo, Vk.MaxDriverInfoSize);
        string deviceUuid = Hex(idProperties.DeviceUuid, Vk.UuidSize);
        string driverUuid = Hex(idProperties.DriverUuid, Vk.UuidSize);
        MetalFingerprint metal = CaptureMetalFingerprint();
        string backend = context.Backend.ToString();
        string skiaBackend = context.GetType().FullName switch
        {
            "Beutl.Graphics.Backend.Composite.CompositeContext" => "Metal",
            "Beutl.Graphics.Backend.Vulkan.VulkanContext" => "Vulkan",
            _ => backend,
        };
        string osBuild = OperatingSystem.IsMacOS()
            ? RunProcess("/usr/bin/sw_vers", ["-buildVersion"]).Trim()
            : ReadOsBuild();

        var result = new RenderPipelineEvidenceFingerprint
        {
            OsDescription = RuntimeInformation.OSDescription,
            OsVersion = Environment.OSVersion.VersionString,
            OsBuild = osBuild,
            OsArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier,
            FrameworkDescription = RuntimeInformation.FrameworkDescription,
            EnvironmentVersion = Environment.Version.ToString(),
            RendererBackend = backend,
            SkiaBackend = skiaBackend,
            DeviceSelection = "automatic-no-preferred-device",
            VulkanApiVersion = DecodeVulkanVersion(properties.ApiVersion),
            VulkanVendorId = $"0x{properties.VendorID:x8}",
            VulkanDeviceId = $"0x{properties.DeviceID:x8}",
            VulkanDeviceType = properties.DeviceType.ToString(),
            VulkanDeviceName = deviceName,
            VulkanDeviceUuid = deviceUuid,
            VulkanDriverUuid = driverUuid,
            VulkanDriverId = driverProperties.DriverID.ToString(),
            VulkanDriverName = driverName,
            VulkanDriverInfo = driverInfo,
            VulkanDriverVersionRaw = properties.DriverVersion.ToString(),
            VulkanDriverVersionDecoded = DecodeVulkanVersion(properties.DriverVersion),
            VulkanEnabledExtensions = GraphicsContextFactory.GetEnabledExtensions()
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            MetalDeviceName = metal.DeviceName,
            MetalRegistryId = metal.RegistryId,
            MetalFeatureFamily = metal.FeatureFamily,
            MetalDriver = OperatingSystem.IsMacOS()
                ? $"Apple Metal shipped with macOS build {osBuild}"
                : "not-applicable",
            SkiaSharpManagedVersion = AssemblyVersion(typeof(SKBitmap).Assembly),
            SkiaSharpNativeVersion = SkiaSharpVersion.Native.ToString(),
            SilkNetVulkanVersion = AssemblyVersion(typeof(Vk).Assembly),
            BeutlEngineAssemblyVersion = AssemblyVersion(typeof(RenderNode).Assembly),
        };
        Validate(result);
        return result;
    }

    private static MetalFingerprint CaptureMetalFingerprint()
    {
        if (!OperatingSystem.IsMacOS())
            return new MetalFingerprint("not-applicable", "not-applicable", "not-applicable");

        IntPtr device = MTLCreateSystemDefaultDevice();
        if (device == IntPtr.Zero)
            throw new InvalidOperationException("MTLCreateSystemDefaultDevice returned null.");
        try
        {
            IntPtr nameObject = IntPtr_objc_msgSend(device, sel_registerName("name"));
            IntPtr utf8 = IntPtr_objc_msgSend(nameObject, sel_registerName("UTF8String"));
            string name = Marshal.PtrToStringUTF8(utf8)
                ?? throw new InvalidOperationException("The Metal device name was null.");
            ulong registryId = UInt64_objc_msgSend(device, sel_registerName("registryID"));
            if (registryId == 0)
                throw new InvalidOperationException("The Metal registry ID was zero.");

            string profiler = RunProcess("/usr/sbin/system_profiler", ["SPDisplaysDataType", "-json"]);
            using JsonDocument document = JsonDocument.Parse(profiler);
            JsonElement gpu = document.RootElement.GetProperty("SPDisplaysDataType")[0];
            string featureFamily = gpu.TryGetProperty("spdisplays_mtlgpufamilysupport", out JsonElement family)
                ? family.GetString()
                    ?? throw new InvalidOperationException("The Metal feature-family value was null.")
                : throw new InvalidOperationException("system_profiler did not report a Metal feature family.");
            return new MetalFingerprint(name, $"0x{registryId:x16}", featureFamily);
        }
        finally
        {
            objc_release(device);
        }
    }

    private static unsafe string FixedUtf8(byte* value, uint maxLength)
    {
        int length = 0;
        while (length < maxLength && value[length] != 0)
            length++;
        return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(value, length));
    }

    private static unsafe string Hex(byte* value, uint length)
        => Convert.ToHexString(new ReadOnlySpan<byte>(value, checked((int)length))).ToLowerInvariant();

    private static string DecodeVulkanVersion(uint value)
        => $"{value >> 22}.{(value >> 12) & 0x3ff}.{value & 0xfff}";

    private static string ReadOsBuild()
    {
        if (OperatingSystem.IsLinux() && File.Exists("/etc/os-release"))
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes("/etc/os-release"))).ToLowerInvariant();
        return Environment.OSVersion.VersionString;
    }

    private static string RunProcess(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{fileName}'.");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'{fileName}' exited with {process.ExitCode}: {stderr.Trim()}");
        }
        return stdout;
    }

    private static string AssemblyVersion(Assembly assembly)
        => assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
           ?? assembly.GetName().Version?.ToString()
           ?? throw new InvalidOperationException($"Assembly '{assembly.FullName}' has no version.");

    private static void Validate(RenderPipelineEvidenceFingerprint fingerprint)
    {
        foreach (PropertyInfo property in typeof(RenderPipelineEvidenceFingerprint).GetProperties())
        {
            object? value = property.GetValue(fingerprint);
            if (value is string text
                && (string.IsNullOrWhiteSpace(text)
                    || text.Contains("unknown", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Fingerprint field '{property.Name}' is missing or unknown.");
            }
            if (value is string[] array
                && (array.Length == 0 || array.Any(string.IsNullOrWhiteSpace)))
            {
                throw new InvalidOperationException($"Fingerprint field '{property.Name}' is empty.");
            }
        }
    }

    [DllImport("/System/Library/Frameworks/Metal.framework/Metal")]
    private static extern IntPtr MTLCreateSystemDefaultDevice();

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern ulong UInt64_objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern void objc_release(IntPtr value);

    private sealed record MetalFingerprint(string DeviceName, string RegistryId, string FeatureFamily);
}
