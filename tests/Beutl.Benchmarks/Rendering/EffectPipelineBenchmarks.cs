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

    [Params("ColorChain", "MixedChain", "SplitTree", "HeavySource")]
    public string Scene = "ColorChain";

    [Params("1080p", "4K")]
    public string Resolution = "1080p";

    private Drawable.Resource _resource = null!;
    private PixelSize _size;
    private PipelineDiagnosticsSnapshot _counters;

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
        _resource = Build(Scene, _size);

        // One render captures the (structure-determined, size-independent) counter snapshot.
        _counters = RenderThread.Dispatcher.Invoke(() => RenderOnce(_resource, _size));
    }

    [Benchmark]
    public void RenderFrame()
    {
        RenderThread.Dispatcher.Invoke(() => RenderOnce(_resource, _size));
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

    // Mirrors GoldenImageHarness.RenderAtScale at output scale 1.0 and returns the always-on counter snapshot.
    private static PipelineDiagnosticsSnapshot RenderOnce(Drawable.Resource resource, PixelSize size)
    {
        using RenderTarget target = RenderTarget.Create(size.Width, size.Height)
                                    ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
        using var canvas = new ImmediateCanvas(target, 1f, logicalSize: size.ToSize(1));
        canvas.Clear(Colors.Black);

        using var node = new DrawableRenderNode(resource);
        using (var ctx = new GraphicsContext2D(node, size.ToSize(1), 1f))
        {
            resource.GetOriginal().Render(ctx, resource);
        }

        var processor = new RenderNodeProcessor(node, useRenderCache: false, outputScale: 1f);
        RenderNodeOperation[] ops = processor.PullToRoot();
        foreach (RenderNodeOperation op in ops)
        {
            op.Render(canvas);
            op.Dispose();
        }

        return processor.Diagnostics.Snapshot();
    }

    // ---- Scene builders (duplicated from SceneFixtures.cs; keep in sync) ----

    private static Drawable.Resource Build(string scene, PixelSize size) => scene switch
    {
        "ColorChain" => ColorChain(size),
        "MixedChain" => MixedChain(size),
        "SplitTree" => SplitTree(size),
        "HeavySource" => HeavySource(size),
        _ => throw new ArgumentOutOfRangeException(nameof(scene), scene, "Unknown O3 scene."),
    };

    private static Drawable.Resource ColorChain(PixelSize size)
    {
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
        group.Children.Add(new Invert());
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
        group.Children.Add(new Invert());
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

    private static Drawable.Resource GradientShape(PixelSize size, FilterEffect effect)
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
        shape.Width.CurrentValue = size.Width * 0.8f;
        shape.Height.CurrentValue = size.Height * 0.8f;
        shape.Fill.CurrentValue = fill;

        var rotation = new RotationTransform();
        rotation.Rotation.CurrentValue = 12f;
        shape.Transform.CurrentValue = rotation;

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
