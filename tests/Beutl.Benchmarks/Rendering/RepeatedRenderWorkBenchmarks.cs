using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

using BenchmarkDotNet.Attributes;

using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shapes;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Primitives;
using Beutl.Graphics3D.Textures;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Benchmarks.Rendering;

/// <summary>
/// Work a renderer repeats from frame to frame, with one persistent renderer per benchmark. The request-wide cache and
/// program counters of each case land in the counters directory, for telling work done from work skipped.
/// </summary>
[Config(typeof(RenderPipelineBenchmarkConfig))]
public abstract class RepeatedRenderBenchmark
{
    private RepeatedRenderSession? _session;

    private protected RepeatedRenderSession Session
        => _session ?? throw new InvalidOperationException("Benchmark setup did not create a render session.");

    private protected abstract RepeatedRenderSession CreateSession();

    private protected abstract string CaseName { get; }

    [GlobalSetup]
    public void Setup()
    {
        _session = RenderThread.Dispatcher.Invoke(CreateSession);
        RenderThread.Dispatcher.Invoke(_session.Warm);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        RepeatedRenderSession? session = Interlocked.Exchange(ref _session, null);
        if (session is null)
            return;

        RenderThread.Dispatcher.Invoke(() =>
        {
            session.WriteCounters(CaseName);
            session.Dispose();
        });
        OnCleanedUp();
    }

    private protected virtual void OnCleanedUp()
    {
    }
}

/// <summary>Planning cost as cache candidates nest: each level is a shader under a node ready to be captured.</summary>
public class NestedCacheCandidateBenchmarks : RepeatedRenderBenchmark
{
    [Params(1, 8, 32)]
    public int Depth { get; set; }

    [Params(false, true)]
    public bool CacheEnabled { get; set; }

    private protected override string CaseName => $"NestedCacheCandidates-{Depth}-{CacheEnabled}";

    private protected override RepeatedRenderSession CreateSession()
    {
        RenderNode current = RepeatedRenderSession.CreateSource(0);
        for (int level = 0; level < Depth; level++)
        {
            current = RenderPipelineBenchmarkSession.WrapShader(
                current, (level & 1) == 0 ? BenchmarkShader.Gamma : BenchmarkShader.Invert);
            var boundary = new BenchmarkCacheBoundaryNode();
            boundary.AddChild(current);
            for (int request = 0; request < RenderNodeCache.StableRequestCount; request++)
                boundary.Cache.RecordSuccessfulStableRequest();
            current = boundary;
        }

        // Keeps every frame a real request while the nest below it stays valid.
        var animated = new BenchmarkAnimatedShaderNode();
        animated.AddChild(current);
        return new RepeatedRenderSession(
            animated,
            RepeatedRenderSession.SmallDomain,
            CacheEnabled ? RenderCacheOptions.Enabled : RenderCacheOptions.Disabled,
            animated);
    }

    [Benchmark]
    public long Rasterize() => RenderThread.Dispatcher.Invoke(Session.Rasterize);
}

/// <summary>Frame and hit-test requests taking turns on stable nodes, which each record the graph of their own.</summary>
public class AlternatingRequestBenchmarks : RepeatedRenderBenchmark
{
    private const int Depth = 32;

    [Params("Frame", "HitTest", "FrameThenHitTest")]
    public string Request { get; set; } = "Frame";

    private protected override string CaseName => $"AlternatingRequests-{Request}";

    private protected override RepeatedRenderSession CreateSession()
    {
        RenderNode current = RepeatedRenderSession.CreateSource(0);
        for (int level = 0; level < Depth; level++)
        {
            current = RenderPipelineBenchmarkSession.WrapShader(
                current, (level & 1) == 0 ? BenchmarkShader.Gamma : BenchmarkShader.Invert);
        }

        return new RepeatedRenderSession(current, RepeatedRenderSession.SmallDomain, RenderCacheOptions.Enabled);
    }

    [Benchmark]
    public long Run()
    {
        return RenderThread.Dispatcher.Invoke(() => Request switch
        {
            "Frame" => Session.Rasterize(),
            "HitTest" => Session.HitTestCenter() ? 1 : -1,
            _ => Session.Rasterize() + (Session.HitTestCenter() ? 1 : -1),
        });
    }
}

/// <summary>A new video frame through static curves or a static LUT, whose lookup images should not be rebuilt.</summary>
public class StaticColorLookupBenchmarks : RepeatedRenderBenchmark
{
    private const int LutSize = 33;
    private string? _cubeFile;

    [Params("None", "Curves", "Lut")]
    public string Effect { get; set; } = string.Empty;

    private protected override string CaseName => $"StaticColorLookup-{Effect}";

    private protected override RepeatedRenderSession CreateSession()
    {
        Rect domain = RenderPipelineBenchmarkScenes.TargetDomain;
        var source = new BenchmarkVideoSourceNode(
            RepeatedRenderSession.CreateSourceTarget(domain, 0),
            RepeatedRenderSession.CreateSourceTarget(domain, 1),
            domain);
        FilterEffect? effect = Effect switch
        {
            "Curves" => new Curves { MasterCurve = { Value = new CurveMap([new(0, 0), new(0.25f, 0.2f), new(0.75f, 0.8f), new(1, 1)]) } },
            "Lut" => CreateLut(),
            _ => null,
        };
        if (effect is null)
        {
            return new RepeatedRenderSession(source, domain, RenderCacheOptions.Disabled, source);
        }

        FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        FilterEffectRenderNode node = resource.CreateRenderNode();
        node.AddChild(source);
        return new RepeatedRenderSession(node, domain, RenderCacheOptions.Disabled, source, resource);
    }

    private LutEffect CreateLut()
    {
        _cubeFile = Path.Combine(Path.GetTempPath(), $"beutl-benchmark-{Guid.NewGuid():N}.cube");
        var text = new StringBuilder($"LUT_3D_SIZE {LutSize}\n");
        for (int b = 0; b < LutSize; b++)
        {
            for (int g = 0; g < LutSize; g++)
            {
                for (int r = 0; r < LutSize; r++)
                {
                    float scale = 1f / (LutSize - 1);
                    text.AppendLine(CultureInfo.InvariantCulture, $"{r * scale} {g * scale} {b * scale * 0.9f}");
                }
            }
        }

        File.WriteAllText(_cubeFile, text.ToString());
        var cube = new CubeSource();
        cube.ReadFrom(new Uri(_cubeFile));
        return new LutEffect { Source = { Value = cube } };
    }

    [Benchmark]
    public long Rasterize() => RenderThread.Dispatcher.Invoke(Session.Rasterize);

    private protected override void OnCleanedUp()
    {
        if (_cubeFile != null)
            File.Delete(_cubeFile);
    }
}

/// <summary>A 3D scene whose materials show static drawables, which are nested requests of each frame.</summary>
public class DrawableTextureBenchmarks : RepeatedRenderBenchmark
{
    [Params(1, 8)]
    public int TextureCount { get; set; }

    [Params(false, true)]
    public bool CacheEnabled { get; set; }

    private protected override string CaseName => $"DrawableTexture-{TextureCount}-{CacheEnabled}";

    private protected override RepeatedRenderSession CreateSession()
    {
        var light = new DirectionalLight3D();
        light.Direction.Value = new Vector3(0, 0, 1);
        light.Intensity.Value = 1;
        light.IsEnabled.Value = true;

        var scene = new Scene3D();
        scene.RenderWidth.Value = 256;
        scene.RenderHeight.Value = 256;
        scene.BackgroundColor.Value = Colors.Black;
        scene.AmbientColor.Value = Colors.White;
        var resource = (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
        resource.Lights.Add((Light3D.Resource)light.ToResource(CompositionContext.Default));
        for (int index = 0; index < TextureCount; index++)
        {
            var drawable = new RectShape();
            drawable.Width.Value = 128;
            drawable.Height.Value = 128;
            drawable.Fill.Value = Brushes.Red;
            var texture = new DrawableTextureSource();
            texture.Drawable.Value = drawable;
            texture.TextureWidth.Value = 128;
            texture.TextureHeight.Value = 128;
            var material = new BasicMaterial();
            material.DiffuseMap.Value = texture;
            var cube = new Cube3D();
            cube.Material.Value = material;
            resource.Objects.Add((Object3D.Resource)cube.ToResource(CompositionContext.Default));
        }

        return new RepeatedRenderSession(
            new Scene3DRenderNode(resource),
            new Rect(0, 0, 256, 256),
            CacheEnabled ? RenderCacheOptions.Enabled : RenderCacheOptions.Disabled,
            null,
            resource);
    }

    [Benchmark]
    public long Rasterize() => RenderThread.Dispatcher.Invoke(Session.Rasterize);
}

internal sealed class RepeatedRenderSession : IDisposable
{
    // Small enough for planning to outweigh pixels.
    public static readonly Rect SmallDomain = new(0, 0, 64, 64);

    private readonly RenderNode _root;
    private readonly RenderNodeRenderer _renderer;
    private readonly IFrameStateConsumer? _animated;
    private readonly IDisposable[] _resources;
    private RenderNodeRasterization? _lastRasterization;
    private int _frame;
    private long _rasterizations;

    public RepeatedRenderSession(
        RenderNode root,
        Rect domain,
        RenderCacheOptions cacheOptions,
        IFrameStateConsumer? animated = null,
        params IDisposable[] resources)
    {
        RenderThread.Dispatcher.VerifyAccess();
        _ = GraphicsContextFactory.GetOrCreateShared()
            ?? throw new InvalidOperationException("A real graphics context is required for render benchmarks.");

        _root = root;
        _animated = animated;
        _resources = resources;
        _renderer = new RenderNodeRenderer(root, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            TargetDomain = domain,
            OutputScale = 1,
            MaxWorkingScale = 1,
            CacheOptions = cacheOptions,
            Purpose = RenderRequestPurpose.Frame,
            FusionMode = RenderPipelineBenchmarkConfig.GetFusionMode(),
        });
    }

    public static RenderTarget CreateSourceTarget(Rect bounds, int variant)
        => RenderPipelineBenchmarkSession.CreateSourceTarget(RenderPipelineBenchmarkScenes.Get("NoEffectControl"), bounds, variant);

    public static RenderNode CreateSource(int variant)
        => new BenchmarkMaterializedSourceNode(CreateSourceTarget(SmallDomain, variant), SmallDomain);

    public void Warm()
    {
        for (int frame = 0; frame < RenderPipelineBenchmarkConfig.SetupWarmupFrameCount; frame++)
        {
            if (Rasterize() == 0)
                throw new InvalidOperationException("The benchmark scene rendered nothing.");
        }
    }

    public long Rasterize()
    {
        _animated?.Apply(new RenderPipelineBenchmarkFrameState(0.5f + (_frame++ % 16) / 32f, false));
        _lastRasterization?.Dispose();
        _lastRasterization = _renderer.Rasterize();
        _rasterizations++;
        return _lastRasterization.Bitmap?.ByteCount ?? 0;
    }

    public bool HitTestCenter()
    {
        Rect? domain = _renderer.DefaultRequest.TargetDomain;
        return _renderer.HitTest(domain?.Center ?? default);
    }

    public void WriteCounters(string caseName)
    {
        SortedDictionary<string, long> counters = RenderPipelineInternalDiagnostics.CaptureLatestCounters(_renderer, out _);
        counters["Rasterizations"] = _rasterizations;
        string directory = RenderPipelineBenchmarkConfig.GetCountersPath();
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, caseName + ".json"),
            JsonSerializer.Serialize(counters, RenderPipelineBenchmarkCounterRecord.JsonOptions) + "\n");
    }

    public void Dispose()
    {
        _lastRasterization?.Dispose();
        _renderer.Dispose();
        _root.Dispose();
        foreach (IDisposable resource in _resources)
            resource.Dispose();
    }
}

/// <summary>A source with a new image each frame, like a decoded video, without the cost of decoding it.</summary>
internal sealed class BenchmarkVideoSourceNode(RenderTarget first, RenderTarget second, Rect bounds)
    : RenderNode, IFrameStateConsumer
{
    private bool _showSecond;

    public void Apply(RenderPipelineBenchmarkFrameState state)
    {
        _showSecond = !_showSecond;
        MarkChanged();
    }

    public override void Process(RenderNodeContext context)
    {
        RenderResource<RenderTarget> resource = context.Borrow(_showSecond ? second : first);
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
        {
            first.Dispose();
            second.Dispose();
        }
    }
}
