using BenchmarkDotNet.Attributes;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;

namespace Beutl.Benchmarks.Rendering;

/// <summary>
/// SC-005 baseline benchmark (feature 004, T009; contracts/observability.md O3). Renders the four O3 scenes
/// at 1920×1080 and 3840×2160 through the legacy effect pipeline and reports median frame time plus a final
/// per-scene <see cref="PipelineDiagnostics"/> snapshot (printed in <see cref="GlobalCleanup"/>; BDN captures
/// the counter numbers as console output). The scene builders duplicate the canonical fixtures in
/// tests/Beutl.UnitTests/Engine/Graphics/Rendering/EffectPipeline/SceneFixtures.cs — the benchmark project
/// cannot reference the test project, so keep the two in sync when a fixture changes.
///
/// A ShortRun job (1 launch, 3 warmup + 3 measured iterations) keeps a full 8-case baseline sweep to a few
/// minutes: each frame is a real GPU render dispatched onto the render thread, so a Default job's iteration
/// count would be wastefully slow without tightening the reported median.
/// </summary>
[ShortRunJob]
[MemoryDiagnoser]
public class EffectPipelineBenchmarks
{
    private const int PatternSeed = 20040705;

    [Params(
        "NoEffect", "SingleBlur", "SingleGamma", "ColorChain", "MixedChain", "SplitTree", "HeavySource",
        "LongChain", "AnimatedUniform", "StructuralToggle", "ShadowStack", "SmallObject")]
    public string Scene = "ColorChain";

    [Params("1080p", "4K")]
    public string Resolution = "1080p";

    private Drawable.Resource _resource = null!;
    private PixelSize _size;
    private PipelineDiagnosticsSnapshot _counters;

    // Mirrors the production Renderer: ONE render-thread-affine pool for the whole run, trimmed once per
    // frame. Without this every effect-pass acquire degenerates to a fresh RenderTarget.Create (a real
    // Vulkan allocation), which the production pipeline never pays in steady state.
    private static readonly RenderTargetPool s_pool =
        RenderThread.Dispatcher.Invoke(static () => new RenderTargetPool());
    private static long s_frameIndex;
    private static DrawableRenderNode? s_node;
    private static Action? s_frameMutator;

    private static void ResetPersistentNode()
    {
        RenderThread.Dispatcher.Invoke(static () =>
        {
            s_node?.Dispose();
            s_node = null;
        });
    }

    // Log.LoggerFactory's setter is internal (not visible to this project); LutEffect's static ctor NREs in
    // Release without it. Seed the backing field so MixedChain can instantiate.
    private static void SeedLoggerFactory()
    {
        System.Reflection.FieldInfo? field = typeof(Log).GetField(
            "s_loggerFactory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        field?.SetValue(null, Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { }));
    }

    private static Uri CreateDataUri(string mediaType, byte[] data)
        => new($"data:{mediaType};base64,{Convert.ToBase64String(data)}");

    private static PixelSize SizeOf(string resolution) => resolution switch
    {
        "1080p" => new PixelSize(1920, 1080),
        "4K" => new PixelSize(3840, 2160),
        _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unknown resolution."),
    };

    // Diagnostic probe (not a benchmark): renders N frames of one scene and prints per-frame wall time and
    // the per-frame counter snapshot, so steady-state pool/program behavior is directly observable.
    internal static void Probe(string scene, string resolution, int frames)
    {
        SeedLoggerFactory();
        RenderThread.Dispatcher.Invoke(() =>
        {
            if (Beutl.Graphics.Backend.GraphicsContextFactory.GetOrCreateShared() == null)
                throw new InvalidOperationException("No Vulkan/MoltenVK context available for the probe.");
        });
        PixelSize size = SizeOf(resolution);
        ResetPersistentNode();
        s_frameMutator = null;
        Drawable.Resource resource = Build(scene, size);
        for (int i = 0; i < frames; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            PipelineDiagnosticsSnapshot snap = RenderThread.Dispatcher.Invoke(() =>
            {
                ApplyFrameMutation(resource);
                return RenderOnce(resource, size);
            });
            sw.Stop();
            Console.WriteLine($"frame {i}: {sw.Elapsed.TotalMilliseconds,7:F2} ms  {snap}");
        }
    }

    [GlobalSetup]
    public void Setup()
    {
        SeedLoggerFactory();
        RenderThread.Dispatcher.Invoke(() =>
        {
            if (Beutl.Graphics.Backend.GraphicsContextFactory.GetOrCreateShared() == null)
                throw new InvalidOperationException("No Vulkan/MoltenVK context available for the benchmark.");
        });

        _size = SizeOf(Resolution);
        ResetPersistentNode();
        s_frameMutator = null;
        _resource = Build(Scene, _size);

        // Warm the persistent render node and pool. Counters are captured by measured RenderFrame invocations.
        RenderThread.Dispatcher.Invoke(() => RenderOnce(_resource, _size));
    }

    [Benchmark]
    public void RenderFrame()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            ApplyFrameMutation(_resource);
            _counters = RenderOnce(_resource, _size);
        });
    }

    private static void ApplyFrameMutation(Drawable.Resource resource)
    {
        if (s_frameMutator == null)
            return;

        s_frameMutator();
        bool updateOnly = false;
        resource.Update(resource.GetOriginal(), CompositionContext.Default, ref updateOnly);
    }

    [GlobalCleanup]
    public void ReportCounters()
    {
        Console.WriteLine(
            $"[counters] {Scene} {Resolution}: GpuPasses={_counters.GpuPasses} "
            + $"TargetAllocations={_counters.TargetAllocations} "
            + $"FullFrameMaterializations={_counters.FullFrameMaterializations} "
            + $"FlushSyncs={_counters.FlushSyncs}");
    }

    // Mirrors the production Renderer's frame loop at output scale 1.0 and returns the always-on counter
    // snapshot: the DrawableRenderNode persists across frames (like Renderer's per-drawable node entry) and the
    // drawable re-render is skipped when the resource is unchanged, so node-level caches (the plan cache above
    // all) engage exactly as they do in the app.
    private static PipelineDiagnosticsSnapshot RenderOnce(Drawable.Resource resource, PixelSize size)
    {
        bool shouldRender;
        DrawableRenderNode node;
        if (s_node == null)
        {
            node = s_node = new DrawableRenderNode(resource);
            shouldRender = true;
        }
        else
        {
            node = s_node;
            shouldRender = node.Update(resource);
        }

        using RenderTarget target = RenderTarget.Create(size.Width, size.Height)
                                    ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
        using var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f, logicalSize: size.ToSize(1));
        canvas.Clear(Colors.Black);

        if (shouldRender)
        {
            using var ctx = new GraphicsContext2D(node, size.ToSize(1), 1f);
            resource.GetOriginal().Render(ctx, resource);
        }

        var processor = new RenderNodeProcessor(
            s_pool, node, useRenderCache: false, RenderIntent.Delivery, outputScale: 1f);
        RenderNodeOperation[] ops = processor.PullToRoot();
        foreach (RenderNodeOperation op in ops)
        {
            op.Render(canvas);
            op.Dispose();
        }

        PipelineDiagnosticsSnapshot snapshot = processor.Diagnostics.Snapshot();
        s_pool.Trim(++s_frameIndex);
        return snapshot;
    }

    // ---- Scene builders (duplicated from SceneFixtures.cs; keep in sync) ----

    private static Drawable.Resource Build(string scene, PixelSize size) => scene switch
    {
        "NoEffect" => GradientShape(size, null),
        "SingleBlur" => SingleBlur(size),
        "SingleGamma" => SingleGamma(size),
        "LongChain" => LongChain(size),
        "AnimatedUniform" => AnimatedUniform(size),
        "StructuralToggle" => StructuralToggle(size),
        "ShadowStack" => ShadowStack(size),
        "SmallObject" => SmallObject(size),
        "ColorChain" => ColorChain(size),
        "MixedChain" => MixedChain(size),
        "SplitTree" => SplitTree(size),
        "HeavySource" => HeavySource(size),
        _ => throw new ArgumentOutOfRangeException(nameof(scene), scene, "Unknown O3 scene."),
    };

    private static Drawable.Resource SingleBlur(PixelSize size)
    {
        var blur = new Blur();
        blur.Sigma.CurrentValue = new Size(6, 6);
        return GradientShape(size, blur);
    }

    private static Drawable.Resource SingleGamma(PixelSize size)
    {
        var gamma = new Gamma();
        gamma.Amount.CurrentValue = 150f;
        return GradientShape(size, gamma);
    }

    private static Drawable.Resource LongChain(PixelSize size)
    {
        var group = new FilterEffectGroup();
        for (int i = 0; i < 4; i++)
        {
            var gamma = new Gamma();
            gamma.Amount.CurrentValue = 110f;
            group.Children.Add(gamma);
            var hue = new HueRotate();
            hue.Angle.CurrentValue = 15f * (i + 1);
            group.Children.Add(hue);
            var saturate = new Saturate();
            saturate.Amount.CurrentValue = 105f;
            group.Children.Add(saturate);
            var brightness = new Brightness();
            brightness.Amount.CurrentValue = 102f;
            group.Children.Add(brightness);
        }

        return GradientShape(size, group);
    }

    // The ColorChain with the Gamma amount changed every frame (an animated adjustment): the mutator runs before
    // each RenderFrame, followed by a resource Update, so the render sees a per-frame parameter change.
    private static Drawable.Resource AnimatedUniform(PixelSize size)
    {
        var group = new FilterEffectGroup();
        var gamma = new Gamma();
        gamma.Amount.CurrentValue = 150f;
        group.Children.Add(gamma);
        var hue = new HueRotate();
        hue.Angle.CurrentValue = 90f;
        group.Children.Add(hue);
        var saturate = new Saturate();
        saturate.Amount.CurrentValue = 140f;
        group.Children.Add(saturate);
        var brightness = new Brightness();
        brightness.Amount.CurrentValue = 120f;
        group.Children.Add(brightness);
        var invert = new Invert();
        invert.Amount.CurrentValue = 100f;
        group.Children.Add(invert);
        long tick = 0;
        s_frameMutator = () => gamma.Amount.CurrentValue = 120f + 60f * (++tick % 60) / 60f;
        return GradientShape(size, group);
    }

    // The ColorChain with the Invert child flipping enabled<->disabled every frame: a per-frame STRUCTURAL change,
    // the worst case for a plan-cached pipeline (the legacy pipeline rebuilt everything per frame regardless).
    private static Drawable.Resource StructuralToggle(PixelSize size)
    {
        var group = new FilterEffectGroup();
        var gamma = new Gamma();
        gamma.Amount.CurrentValue = 150f;
        group.Children.Add(gamma);
        var hue = new HueRotate();
        hue.Angle.CurrentValue = 90f;
        group.Children.Add(hue);
        var saturate = new Saturate();
        saturate.Amount.CurrentValue = 140f;
        group.Children.Add(saturate);
        var brightness = new Brightness();
        brightness.Amount.CurrentValue = 120f;
        group.Children.Add(brightness);
        var invert = new Invert();
        invert.Amount.CurrentValue = 100f;
        group.Children.Add(invert);
        long tick = 0;
        s_frameMutator = () => invert.IsEnabled = (++tick & 1) == 0;
        return GradientShape(size, group);
    }

    private static Drawable.Resource ShadowStack(PixelSize size)
    {
        var group = new FilterEffectGroup();
        for (int i = 1; i <= 3; i++)
        {
            var shadow = new DropShadow();
            shadow.Position.CurrentValue = new Point(6 * i, 6 * i);
            shadow.Sigma.CurrentValue = new Size(4 * i, 4 * i);
            shadow.Color.CurrentValue = Colors.Black;
            group.Children.Add(shadow);
        }

        return GradientShape(size, group);
    }

    // The ColorChain on a shape covering ~1% of the frame area: per-pass fixed overhead dominates over pixel work.
    private static Drawable.Resource SmallObject(PixelSize size)
    {
        var group = new FilterEffectGroup();
        var gamma = new Gamma();
        gamma.Amount.CurrentValue = 150f;
        group.Children.Add(gamma);
        var hue = new HueRotate();
        hue.Angle.CurrentValue = 90f;
        group.Children.Add(hue);
        var saturate = new Saturate();
        saturate.Amount.CurrentValue = 140f;
        group.Children.Add(saturate);
        var brightness = new Brightness();
        brightness.Amount.CurrentValue = 120f;
        group.Children.Add(brightness);
        var invert = new Invert();
        invert.Amount.CurrentValue = 100f;
        group.Children.Add(invert);
        return GradientShape(size, group, sizeFactor: 0.1f);
    }

    private static Drawable.Resource ColorChain(PixelSize size)
    {
        // These four official O3 builders duplicate SceneFixtures intentionally. Their parameter values are pinned to
        // the workload used by notes/baseline.md; changing them invalidates the recorded SC-005 comparison.
        var group = new FilterEffectGroup();
        var gamma = new Gamma();
        gamma.Amount.CurrentValue = 1.5f;
        group.Children.Add(gamma);
        var hue = new HueRotate();
        hue.Angle.CurrentValue = 90f;
        group.Children.Add(hue);
        var saturate = new Saturate();
        saturate.Amount.CurrentValue = 1.4f;
        group.Children.Add(saturate);
        var brightness = new Brightness();
        brightness.Amount.CurrentValue = 1.2f;
        group.Children.Add(brightness);
        var invert = new Invert();
        invert.Amount.CurrentValue = 1f;
        group.Children.Add(invert);
        return GradientShape(size, group);
    }

    private static Drawable.Resource MixedChain(PixelSize size)
    {
        var group = new FilterEffectGroup();
        var blur = new Blur();
        blur.Sigma.CurrentValue = new Size(6, 6);
        group.Children.Add(blur);
        var gamma = new Gamma();
        gamma.Amount.CurrentValue = 1.4f;
        group.Children.Add(gamma);
        var invert = new Invert();
        invert.Amount.CurrentValue = 1f;
        group.Children.Add(invert);
        var dropShadow = new DropShadow();
        dropShadow.Position.CurrentValue = new Point(8, 8);
        dropShadow.Sigma.CurrentValue = new Size(6, 6);
        dropShadow.Color.CurrentValue = Colors.Black;
        group.Children.Add(dropShadow);
        var lut = new LutEffect();
        lut.Source.CurrentValue = CreateInvertLutSource();
        group.Children.Add(lut);
        return GradientShape(size, group);
    }

    private static Drawable.Resource SplitTree(PixelSize size)
    {
        var split = new SplitEffect();
        split.HorizontalDivisions.CurrentValue = 3;
        split.VerticalDivisions.CurrentValue = 3;
        split.HorizontalSpacing.CurrentValue = 10;
        split.VerticalSpacing.CurrentValue = 10;

        var saturate = new Saturate();
        saturate.Amount.CurrentValue = 1.5f;

        var group = new FilterEffectGroup();
        group.Children.Add(split);
        group.Children.Add(saturate);
        group.Children.Add(new LayerEffect());
        return GradientShape(size, group);
    }

    private static Drawable.Resource HeavySource(PixelSize size)
    {
        var group = new FilterEffectGroup();
        var gamma = new Gamma();
        gamma.Amount.CurrentValue = 1.3f;
        group.Children.Add(gamma);
        var invert = new Invert();
        invert.Amount.CurrentValue = 1f;
        group.Children.Add(invert);
        var grading = new ColorGrading();
        grading.Contrast.CurrentValue = 1.2f;
        grading.Saturation.CurrentValue = 1.3f;
        group.Children.Add(grading);

        var imageSource = new ImageSource();
        imageSource.ReadFrom(CreatePatternImageUri(size.Width, size.Height));

        var image = new SourceImage();
        image.Source.CurrentValue = imageSource;
        image.FilterEffect.CurrentValue = group;
        return image.ToResource(CompositionContext.Default);
    }

    private static CubeSource CreateInvertLutSource()
    {
        const string cubeText =
            """
            TITLE "004 invert"
            LUT_3D_SIZE 2
            DOMAIN_MIN 0 0 0
            DOMAIN_MAX 1 1 1
            1 1 1
            0 1 1
            1 0 1
            0 0 1
            1 1 0
            0 1 0
            1 0 0
            0 0 0
            """;
        var source = new CubeSource();
        source.ReadFrom(CreateDataUri("text/plain", System.Text.Encoding.ASCII.GetBytes(cubeText)));
        return source;
    }

    private static Drawable.Resource GradientShape(PixelSize size, FilterEffect? effect, float sizeFactor = 0.8f)
    {
        var fill = new LinearGradientBrush();
        fill.StartPoint.CurrentValue = new RelativePoint(0, 0, RelativeUnit.Relative);
        fill.EndPoint.CurrentValue = new RelativePoint(1, 1, RelativeUnit.Relative);
        fill.GradientStops.Add(new GradientStop(Colors.Red, 0));
        fill.GradientStops.Add(new GradientStop(Colors.Green, 0.5f));
        fill.GradientStops.Add(new GradientStop(Colors.Blue, 1));

        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Width.CurrentValue = size.Width * sizeFactor;
        shape.Height.CurrentValue = size.Height * sizeFactor;
        shape.Fill.CurrentValue = fill;

        var rotation = new RotationTransform();
        rotation.Rotation.CurrentValue = 12f;
        shape.Transform.CurrentValue = rotation;

        if (effect is not null)
            shape.FilterEffect.CurrentValue = effect;
        return shape.ToResource(CompositionContext.Default);
    }

    private static Uri CreatePatternImageUri(int width, int height)
    {
        using var bitmap = new Bitmap(width, height);
        Span<Bgra8888> pixels = bitmap.GetPixelSpan<Bgra8888>();
        var rng = new Random(PatternSeed);
        int tile = Math.Max(8, Math.Min(width, height) / 12);
        int tilesX = (width + tile - 1) / tile;
        int tilesY = (height + tile - 1) / tile;
        var tileColors = new Bgra8888[tilesX * tilesY];
        for (int i = 0; i < tileColors.Length; i++)
        {
            tileColors[i] = new Bgra8888((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), 255);
        }

        for (int y = 0; y < height; y++)
        {
            int ty = y / tile;
            for (int x = 0; x < width; x++)
            {
                int tx = x / tile;
                pixels[y * width + x] = tileColors[ty * tilesX + tx];
            }
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, EncodedImageFormat.Png);
        return CreateDataUri("image/png", stream.ToArray());
    }
}
