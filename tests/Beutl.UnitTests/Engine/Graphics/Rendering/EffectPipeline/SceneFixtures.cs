using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.Serialization;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Deterministic builders for the four O3 effect-pipeline scenes (contracts/observability.md O3):
/// ColorChain, MixedChain, SplitTree, HeavySource. All parameters and the procedural source bitmap use
/// fixed values / a fixed seed so a render is reproducible across runs and machines. Each builder takes a
/// frame <see cref="PixelSize"/>; the <see cref="Hd1080"/> / <see cref="Uhd4K"/> constants are the two
/// benchmark variants, and <see cref="ReferenceSize"/> is the small size used to freeze parity references.
/// </summary>
internal static class SceneFixtures
{
    /// <summary>1920×1080 benchmark variant.</summary>
    public static readonly PixelSize Hd1080 = new(1920, 1080);

    /// <summary>3840×2160 benchmark variant.</summary>
    public static readonly PixelSize Uhd4K = new(3840, 2160);

    /// <summary>Small canvas for frozen parity references (keeps committed reference blobs tiny).</summary>
    public static readonly PixelSize ReferenceSize = new(384, 216);

    private const int PatternSeed = 20040705;

    /// <summary>Names of the four O3 scenes, in observability-contract order.</summary>
    public static IReadOnlyList<string> SceneNames { get; } = ["ColorChain", "MixedChain", "SplitTree", "HeavySource"];

    /// <summary>Builds the named scene at <paramref name="size"/>.</summary>
    public static Drawable.Resource Build(string sceneName, PixelSize size) => sceneName switch
    {
        "ColorChain" => ColorChain(size),
        "MixedChain" => MixedChain(size),
        "SplitTree" => SplitTree(size),
        "HeavySource" => HeavySource(size),
        _ => throw new ArgumentOutOfRangeException(nameof(sceneName), sceneName, "Unknown O3 scene."),
    };

    /// <summary>Five coordinate-invariant color effects over a gradient shape — the fusable-run scene.</summary>
    public static Drawable.Resource ColorChain(PixelSize size)
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

        return GradientShape(size, group);
    }

    /// <summary>Blur → Gamma → Invert → DropShadow → LUT — the interleaved spatial/color scene.</summary>
    public static Drawable.Resource MixedChain(PixelSize size)
    {
        var group = new FilterEffectGroup();
        var blur = new Blur();
        blur.Sigma.CurrentValue = new Size(6, 6);
        group.Children.Add(blur);
        var gamma = new Gamma();
        gamma.Amount.CurrentValue = 140f;
        group.Children.Add(gamma);
        var invert = new Invert();
        invert.Amount.CurrentValue = 100f;
        group.Children.Add(invert);
        var dropShadow = new DropShadow();
        dropShadow.Position.CurrentValue = new Point(8, 8);
        dropShadow.Sigma.CurrentValue = new Size(6, 6);
        dropShadow.Color.CurrentValue = Colors.Black;
        group.Children.Add(dropShadow);
        // Without a CubeSource the LUT renders identity, making the frozen reference blind to a no-op regression.
        var lut = new LutEffect();
        lut.Source.CurrentValue = CreateInvertLutSource();
        group.Children.Add(lut);

        return GradientShape(size, group);
    }

    /// <summary>SplitEffect (3×3) → per-branch color → LayerEffect composite — the split/composite tree scene.</summary>
    public static Drawable.Resource SplitTree(PixelSize size)
    {
        var split = new SplitEffect();
        split.HorizontalDivisions.CurrentValue = 3;
        split.VerticalDivisions.CurrentValue = 3;
        split.HorizontalSpacing.CurrentValue = 10;
        split.VerticalSpacing.CurrentValue = 10;

        var saturate = new Saturate();
        saturate.Amount.CurrentValue = 150f;

        var group = new FilterEffectGroup();
        group.Children.Add(split);
        group.Children.Add(saturate);
        group.Children.Add(new LayerEffect());

        return GradientShape(size, group);
    }

    /// <summary>A frame-sized procedural bitmap driven through three shader effects — the heavy-source scene.</summary>
    public static Drawable.Resource HeavySource(PixelSize size)
    {
        var group = new FilterEffectGroup();
        var gamma = new Gamma();
        gamma.Amount.CurrentValue = 130f;
        group.Children.Add(gamma);
        group.Children.Add(new Invert());
        var grading = new ColorGrading();
        grading.Contrast.CurrentValue = 20f;
        grading.Saturation.CurrentValue = 30f;
        group.Children.Add(grading);

        var imageSource = new ImageSource();
        imageSource.ReadFrom(CreatePatternImageUri(size.Width, size.Height));

        var image = new SourceImage();
        image.Source.CurrentValue = imageSource;
        image.FilterEffect.CurrentValue = group;
        return image.ToResource(CompositionContext.Default);
    }

    /// <summary>
    /// A fixed 2×2×2 channel-inverting .cube LUT (output = 1 − input), delivered as a data URI.
    /// Guarantees a LutEffect reference is visibly non-identity so the parity gate catches a no-op regression.
    /// </summary>
    public static CubeSource CreateInvertLutSource()
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
        source.ReadFrom(UriHelper.CreateBase64DataUri("text/plain", System.Text.Encoding.ASCII.GetBytes(cubeText)));
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

    // A deterministic tile pattern encoded as a PNG data URI. The tiling and colors are derived from a fixed
    // seed so the "large bitmap" source is byte-reproducible across runs.
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
            tileColors[i] = new Bgra8888(
                (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), 255);
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
        return UriHelper.CreateBase64DataUri("image/png", stream.ToArray());
    }
}
