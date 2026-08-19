using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.Serialization;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public sealed class BitmapSamplingQualityTests
{
    [Test]
    public void ExactTwoByTwoReduction_PreservesBlackAndWhiteBlocks()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource source = CreateSourceImage(
                64,
                64,
                static (x, y) => (((x / 2) + (y / 2)) & 1) == 0 ? byte.MinValue : byte.MaxValue);
            using Bitmap rendered = GoldenImageHarness.RenderAtScale(source, new PixelSize(64, 64), 0.5f);

            var observed = new HashSet<float>();
            for (int y = 2; y < rendered.Height - 2; y++)
            {
                for (int x = 2; x < rendered.Width - 2; x++)
                {
                    observed.Add(ReadRed(rendered, x, y));
                }
            }

            TestContext.WriteLine($"Exact 2x2 reduction values: {string.Join(", ", observed.Order())}");
            Assert.That(observed, Is.EquivalentTo(new[] { 0f, 1f }),
                "Each destination pixel covers one uniform source block and must retain its exact endpoint.");
        });
    }

    [Test]
    public void ExactTwoByTwoReduction_PreservesMidToneBlocksWithoutRinging()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource source = CreateSourceImage(
                64,
                64,
                static (x, y) => (((x / 2) + (y / 2)) & 1) == 0 ? (byte)96 : (byte)176);
            using Bitmap rendered = GoldenImageHarness.RenderAtScale(source, new PixelSize(64, 64), 0.5f);

            float[] observed = ReadInteriorRedValues(rendered);
            float low = Color.FromRgb(96, 96, 96).ToLinear().X;
            float high = Color.FromRgb(176, 176, 176).ToLinear().X;
            TestContext.WriteLine($"Exact mid-tone reduction values: {string.Join(", ", observed)}");
            Assert.Multiple(() =>
            {
                Assert.That(observed, Has.Length.EqualTo(2));
                Assert.That(observed[0], Is.EqualTo(low).Within(0.0001f));
                Assert.That(observed[1], Is.EqualTo(high).Within(0.0001f));
            });
        });
    }

    [Test]
    public void MildCheckerboardMinification_RetainsBranchAntialiasing()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource source = CreateSourceImage(
                64,
                64,
                static (x, y) => ((x + y) & 1) == 0 ? byte.MinValue : byte.MaxValue);
            using Bitmap rendered = GoldenImageHarness.RenderAtScale(source, new PixelSize(64, 64), 0.75f);

            float[] observed = ReadInteriorRedValues(rendered);
            float spread = observed[^1] - observed[0];
            TestContext.WriteLine($"0.75x checker values: {string.Join(", ", observed)}; spread={spread:R}");
            Assert.That(spread, Is.LessThanOrEqualTo(0.26f),
                "Non-integer minification must retain the branch's lower-aliasing two-stage path.");
        });
    }

    [Test]
    public void FusedCurrentPixelMagnification_InterpolatesBetweenSourcePixels()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource source = CreateSourceImage(
                2,
                1,
                static (x, _) => x == 0 ? byte.MinValue : byte.MaxValue,
                new FilterEffectGroup
                {
                    Children =
                    {
                        // Non-identity: an identity colour matrix records no stage, so the fixture
                        // would stop exercising the fused colour-stage path it asserts on.
                        CreateBrightness(75f),
                        CreateBrightness(80f),
                    },
                });
            using RenderResult result = Render(source, new PixelSize(2, 1), 4f);

            float[] values = Enumerable.Range(1, result.Bitmap.Width - 2)
                .Select(x => ReadRed(result.Bitmap, x, result.Bitmap.Height / 2))
                .ToArray();
            int distinct = values.Distinct().Count();
            TestContext.WriteLine(
                $"Fused 4x magnification values: {string.Join(", ", values)}; "
                + $"runs={result.Statistics.ShaderRunExecutions}, "
                + $"stages={result.Statistics.ShaderStageExecutions}, "
                + $"fused={result.Statistics.FusedShaderRunExecutions}");
            Assert.Multiple(() =>
            {
                Assert.That(result.Statistics.FusedShaderRunExecutions, Is.EqualTo(1),
                    "The fixture must exercise the fused colour-stage path.");
                Assert.That(result.Statistics.ShaderStageExecutions, Is.GreaterThanOrEqualTo(2));
                Assert.That(distinct, Is.GreaterThan(2),
                    "Magnification must interpolate instead of repeating two nearest-neighbour plateaus.");
                Assert.That(values, Has.Some.GreaterThan(0f).And.LessThan(1f));
            });
        });
    }

    private static Brightness CreateBrightness(float amount)
    {
        var brightness = new Brightness();
        brightness.Amount.CurrentValue = amount;
        return brightness;
    }

    private static Drawable.Resource CreateSourceImage(
        int width,
        int height,
        Func<int, int, byte> red,
        FilterEffect? effect = null)
    {
        using var bitmap = new Bitmap(
            width,
            height,
            BitmapColorType.Bgra8888,
            BitmapAlphaType.Opaque,
            BitmapColorSpace.Srgb);
        for (int y = 0; y < height; y++)
        {
            Span<Bgra8888> row = bitmap.GetRow<Bgra8888>(y);
            for (int x = 0; x < width; x++)
            {
                byte value = red(x, y);
                row[x] = new Bgra8888(value, value, value, byte.MaxValue);
            }
        }

        using var stream = new MemoryStream();
        Assert.That(bitmap.Save(stream, EncodedImageFormat.Png), Is.True);
        var imageSource = new ImageSource();
        imageSource.ReadFrom(UriHelper.CreateBase64DataUri("image/png", stream.ToArray()));
        var image = new SourceImage
        {
            Source = { CurrentValue = imageSource },
            AlignmentX = { CurrentValue = AlignmentX.Left },
            AlignmentY = { CurrentValue = AlignmentY.Top },
            FilterEffect = { CurrentValue = effect },
        };
        return image.ToResource(CompositionContext.Default);
    }

    private static RenderResult Render(Drawable.Resource source, PixelSize frame, float scale)
    {
        using var node = new DrawableRenderNode(source);
        using (var context = new GraphicsContext2D(node, frame.ToSize(1), scale))
        {
            source.GetOriginal().Render(context, source);
        }

        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Delivery,
                    TargetDomain = new Rect(default, frame.ToSize(1)),
                    OutputScale = scale,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                    FusionMode = FusionMode.Enabled,
                },
            });
        using RenderTarget target = RenderTarget.Create(
            (int)MathF.Ceiling(frame.Width * scale),
            (int)MathF.Ceiling(frame.Height * scale))
            ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
        using var canvas = new ImmediateCanvas(target, scale, logicalSize: frame.ToSize(1));
        canvas.Clear(Colors.Black);
        renderer.Render(canvas);
        return new RenderResult(target.Snapshot(), renderer.LastExecutionStatistics);
    }

    private static float ReadRed(Bitmap bitmap, int x, int y)
    {
        ReadOnlySpan<ushort> row = bitmap.GetRow<ushort>(y);
        return (float)BitConverter.UInt16BitsToHalf(row[x * 4]);
    }

    private static float[] ReadInteriorRedValues(Bitmap bitmap)
    {
        var observed = new HashSet<float>();
        for (int y = 2; y < bitmap.Height - 2; y++)
        {
            for (int x = 2; x < bitmap.Width - 2; x++)
            {
                observed.Add(ReadRed(bitmap, x, y));
            }
        }

        return observed.Order().ToArray();
    }

    private sealed record RenderResult(Bitmap Bitmap, RenderExecutionStatistics Statistics) : IDisposable
    {
        public void Dispose() => Bitmap.Dispose();
    }
}
