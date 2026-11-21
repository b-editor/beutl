using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
[NonParallelizable]
public sealed class FilmGrainTests
{
    private const int Width = 128;
    private const int Height = 96;

    [Test]
    public void Zero_amount_passes_the_input_through()
    {
        byte[] before = Render(TranslucentCircle(null), scale: 0.75f);
        byte[] after = Render(TranslucentCircle(new FilmGrain { Amount = { Value = 0 } }), scale: 0.75f);

        Assert.That(after, Is.EqualTo(before));
    }

    [TestCase(float.NaN, 1.5f, 50f, 0f)]
    [TestCase(25f, float.NaN, 50f, 0f)]
    [TestCase(25f, 1.5f, float.NaN, 0f)]
    [TestCase(25f, 1.5f, 50f, float.NaN)]
    public void Non_finite_values_record_no_pass(float amount, float size, float softness, float color)
    {
        var effect = new FilmGrain
        {
            Amount = { Value = amount },
            Size = { Value = size },
            Softness = { Value = softness },
            Color = { Value = color }
        };
        using FilmGrain.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(new Rect(0, 0, Width, Height));

        context.ApplyTransactional(resource);

        Assert.That(context.GetOrderedItems(), Is.Empty);
    }

    [Test]
    public void Flat_gray_gains_texture_and_keeps_its_mean()
    {
        (double baseline, _) = Measure(Channel(Render(GrayRectangle(null)), 1));

        (double mean, double deviation) = Measure(Channel(Render(GrayRectangle(new FilmGrain())), 1));

        Assert.Multiple(() =>
        {
            Assert.That(mean, Is.EqualTo(baseline).Within(1));
            Assert.That(deviation, Is.GreaterThan(3));
        });
    }

    [Test]
    public void Midtones_get_more_grain_than_shadows_and_highlights()
    {
        double DeviationAt(byte shade) => Measure(Channel(Render(GrayRectangle(new FilmGrain(), shade)), 1)).Deviation;

        double midtones = DeviationAt(128);
        double shadows = DeviationAt(16);
        double highlights = DeviationAt(239);

        Assert.Multiple(() =>
        {
            Assert.That(midtones, Is.GreaterThan(2 * shadows));
            Assert.That(midtones, Is.GreaterThan(2 * highlights));
        });
    }

    [Test]
    public void Seed_and_frame_make_the_pattern_reproducible()
    {
        var rate = new Rational(30_000, 1001);
        var time = new CompositionContext(MediaTime.FromFrames(9, rate)) { FrameRate = rate };

        byte[] AtSeed(int seed) => Render(GrayRectangle(new FilmGrain { Seed = { Value = seed } }), context: time);

        byte[] first = AtSeed(int.MinValue);
        byte[] repeated = AtSeed(int.MinValue);
        byte[] different = AtSeed(int.MaxValue);

        Assert.Multiple(() =>
        {
            Assert.That(repeated, Is.EqualTo(first));
            Assert.That(different, Is.Not.EqualTo(first));
        });
    }

    [Test]
    public void Grain_changes_at_clip_local_frame_boundaries_when_animated()
    {
        var rate = new Rational(30_000, 1001);
        MediaTime frame = MediaTime.FromFrames(1, rate);
        MediaTime start = MediaTime.FromFrames(23, rate);
        var effect = new FilmGrain();
        RectShape shape = GrayRectangle(effect);
        var clip = new Element { Start = { Value = start }, Length = { Value = MediaTime.FromSeconds(2) } };
        new TestHierarchicalRoot().HierarchicalChildren.Add(clip);
        clip.AddObject(shape);

        byte[] At(MediaTime time) => Render(shape, context: new CompositionContext(time) { FrameRate = rate });

        byte[] first = At(start);
        byte[] justBefore = At(start + frame - new MediaTime(1));
        byte[] second = At(start + frame);
        MediaTime movedStart = MediaTime.FromFrames(127, rate);
        clip.Start.Value = movedStart;
        byte[] moved = At(movedStart + frame);
        effect.Animate.Value = false;
        byte[] stillFirst = At(movedStart);
        byte[] stillSecond = At(movedStart + frame);

        Assert.Multiple(() =>
        {
            Assert.That(justBefore, Is.EqualTo(first));
            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(moved, Is.EqualTo(second));
            Assert.That(stillSecond, Is.EqualTo(stillFirst));
        });
    }

    [Test]
    public void The_resource_version_tracks_frame_changes_only_while_animated()
    {
        // One frame at this rate rounds down to a whole flick. It must still pick frame 1.
        var rate = new Rational(59, 1);
        MediaTime frame = MediaTime.FromFrames(1, rate);
        var context = new CompositionContext(MediaTime.Zero) { FrameRate = rate };
        var effect = new FilmGrain();
        using FilmGrain.Resource resource = effect.ToResource(context);

        int ReconcileAt(MediaTime time)
        {
            context.Time = time;
            bool versionBumped = false;
            resource.Reconcile(effect, context, ref versionBumped);
            return resource.Version;
        }

        int first = resource.Version;
        int justBefore = ReconcileAt(frame - new MediaTime(1));
        int second = ReconcileAt(frame);
        effect.Animate.Value = false;
        int stopped = ReconcileAt(frame);
        int later = ReconcileAt(MediaTime.FromFrames(2, rate));

        Assert.Multiple(() =>
        {
            Assert.That(justBefore, Is.EqualTo(first));
            Assert.That(second, Is.EqualTo(first + 1));
            Assert.That(later, Is.EqualTo(stopped));
        });
    }

    [Test]
    public void Color_controls_whether_the_channels_share_grain()
    {
        byte[] mono = Render(GrayRectangle(new FilmGrain { Color = { Value = 0 } }));
        byte[] color = Render(GrayRectangle(new FilmGrain { Color = { Value = 100 } }));

        Assert.Multiple(() =>
        {
            Assert.That(Channel(mono, 0), Is.EqualTo(Channel(mono, 1)));
            Assert.That(Channel(mono, 2), Is.EqualTo(Channel(mono, 1)));
            Assert.That(Channel(color, 0), Is.Not.EqualTo(Channel(color, 1)));
            Assert.That(Channel(color, 1), Is.Not.EqualTo(Channel(color, 2)));
            Assert.That(Channel(color, 2), Is.Not.EqualTo(Channel(color, 0)));
        });
    }

    [Test]
    public void Skia_raster_and_Vulkan_render_the_same_pattern()
    {
        RectShape shape = GrayRectangle(new FilmGrain { Size = { Value = 3 }, Color = { Value = 100 } });
        byte[] raster = Render(shape, onCpu: true);
        byte[] vulkan = Render(shape);

        Assert.Multiple(() =>
        {
            Assert.That(Measure(Channel(raster, 1)).Deviation, Is.GreaterThan(3));
            Assert.That(vulkan, Is.EqualTo(raster).Within(1), "allow rounding differences in the sRGB transfer");
        });
    }

    [Test]
    public void Larger_cells_correlate_neighboring_pixels()
    {
        byte[] fine = Render(GrayRectangle(new FilmGrain { Size = { Value = 1 }, Softness = { Value = 0 } }));
        byte[] coarse = Render(GrayRectangle(new FilmGrain { Size = { Value = 4 }, Softness = { Value = 0 } }));

        double fineCorrelation = HorizontalCorrelation(Channel(fine, 1));
        double coarseCorrelation = HorizontalCorrelation(Channel(coarse, 1));

        Assert.Multiple(() =>
        {
            Assert.That(fineCorrelation, Is.EqualTo(0).Within(0.1));
            Assert.That(coarseCorrelation, Is.GreaterThan(fineCorrelation + 0.5));
        });
    }

    [Test]
    public void The_default_grain_has_similar_strength_at_half_scale()
    {
        RectShape shape = GrayRectangle(new FilmGrain());
        double full = Measure(Channel(Render(shape), 1)).Deviation;
        double half = Measure(Channel(Render(shape, scale: 0.5f), 1)).Deviation;

        Assert.That(half, Is.EqualTo(full).Within(20).Percent);
    }

    [Test]
    public void Subpixel_grain_is_attenuated_by_its_device_size()
    {
        RectShape shape = GrayRectangle(new FilmGrain { Size = { Value = 0.5f }, Softness = { Value = 100 } });
        double full = Measure(Channel(Render(shape), 1)).Deviation;
        double half = Measure(Channel(Render(shape, scale: 0.5f), 1)).Deviation;

        Assert.That(half, Is.EqualTo(full / 2).Within(10).Percent);
    }

    [TestCase(1f, 1.5f)]
    [TestCase(0.5f, 1.5f)]
    // Cells smaller than a device pixel take the per-pixel path, which must place its cells the same way.
    [TestCase(1f, 0.5f)]
    [TestCase(0.5f, 0.25f)]
    public void Rendering_a_region_matches_cropping_the_full_render(float scale, float size)
    {
        RectShape shape = GrayRectangle(new FilmGrain { Size = { Value = size } });
        var region = new Rect(22, 18, 64, 44);

        byte[] full = Render(shape, scale);
        byte[] part = Render(shape, scale, region);

        int stride = (int)(Width * scale) * 4;
        var pixels = PixelRect.FromRect(region, scale);
        Assert.That(part[3], Is.Zero, "the area outside the requested region stays clear");
        for (int y = pixels.Y; y < pixels.Y + pixels.Height; y++)
        {
            int offset = y * stride + pixels.X * 4;
            Assert.That(part.AsSpan(offset, pixels.Width * 4).SequenceEqual(full.AsSpan(offset, pixels.Width * 4)),
                Is.True, $"row {y}");
        }
    }

    [Test]
    public void Grain_changes_color_without_changing_alpha()
    {
        byte[] before = Render(TranslucentCircle(null));
        byte[] after = Render(TranslucentCircle(new FilmGrain()));
        byte[] alpha = Channel(after, 3);

        Assert.Multiple(() =>
        {
            Assert.That(alpha, Has.Some.Zero);
            Assert.That(alpha, Has.Some.EqualTo(160));
            Assert.That(alpha, Is.EqualTo(Channel(before, 3)));
            Assert.That(Channel(after, 1), Is.Not.EqualTo(Channel(before, 1)));
            Assert.That(after.Where((_, i) => alpha[i / 4] == 0), Is.All.Zero);
        });
    }

    private static RectShape GrayRectangle(FilmGrain? effect, byte shade = 128) => new()
    {
        Width = { Value = Width },
        Height = { Value = Height },
        FilterEffect = { Value = effect },
        Fill = { Value = new SolidColorBrush(Color.FromRgb(shade, shade, shade)) }
    };

    private static EllipseShape TranslucentCircle(FilmGrain? effect) => new()
    {
        Width = { Value = 45 },
        Height = { Value = 45 },
        FilterEffect = { Value = effect },
        Fill = { Value = new SolidColorBrush(Color.FromArgb(160, 200, 100, 50)) }
    };

    private static byte[] Render(
        Drawable drawable, float scale = 1f, Rect? region = null, CompositionContext? context = null, bool onCpu = false)
    {
        // RenderTarget.Create uses the raster backend off the render thread.
        if (onCpu) return Draw();

        VulkanTestEnvironment.EnsureAvailable();
        return VulkanTestEnvironment.InvokeOnRenderThread(Draw);

        byte[] Draw()
        {
            using var resource = (Drawable.Resource)drawable.ToResource(context ?? CompositionContext.Default);
            using Bitmap rendered = GoldenImageHarness.RenderAtScale(
                resource, new PixelSize(Width, Height), scale, region, clearColor: Colors.Transparent);
            using Bitmap bytes = rendered.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
            int stride = bytes.Width * 4;
            byte[] pixels = new byte[stride * bytes.Height];
            for (int y = 0; y < bytes.Height; y++)
                bytes.GetRow(y).CopyTo(pixels.AsSpan(y * stride, stride));
            return pixels;
        }
    }

    private static byte[] Channel(byte[] pixels, int channel) => pixels.Where((_, i) => i % 4 == channel).ToArray();

    private static (double Mean, double Deviation) Measure(byte[] samples)
    {
        double mean = samples.Average(sample => (double)sample);
        double variance = samples.Average(sample => (sample - mean) * (sample - mean));
        return (mean, Math.Sqrt(variance));
    }

    private static double HorizontalCorrelation(byte[] samples)
    {
        (double mean, double deviation) = Measure(samples);
        double products = 0;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 1; x < Width; x++)
            {
                int index = y * Width + x;
                products += (samples[index - 1] - mean) * (samples[index] - mean);
            }
        }

        return products / ((Width - 1) * Height * deviation * deviation);
    }
}
