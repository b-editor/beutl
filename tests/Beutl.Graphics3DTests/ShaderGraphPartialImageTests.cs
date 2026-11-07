using System.Runtime.InteropServices;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using static Beutl.Graphics3DTests.ShaderGraphContractTests;
using static Beutl.Graphics3DTests.ShaderGraphResourceTests;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class ShaderGraphPartialImageTests
{
    private static readonly Rect s_picture = new(900, 500, 120, 80);
    private static readonly Rect s_frame = new(0, 0, 1920, 1080);

    private static ShaderGraphDescription Grid(int count) => ShaderGraphEffectTests.Record(new RepeatEffect
    {
        Columns = { Value = count },
        Rows = { Value = count },
        Spacing = { Value = new Size(80, 120) },
    });

    private static ShaderImageFrame FrameOf(Rect bounds, float scale = 1)
        => ShaderImageFrame.FromPixels(bounds, PixelRect.FromRect(bounds, scale), scale, default);

    [Test]
    public void TheSameRegionAllocatesTheSameImagesHoweverWideTheOutputIs()
    {
        OnDevice(_ =>
        {
            long[] peaks = [.. new[] { 30, 60, 82, 100 }
                .Select(count => ExecuteIntoTarget(Grid(count), s_picture, region: s_frame).Statistics.PeakResourceBytes)];

            Assert.That(peaks, Is.All.EqualTo(peaks[0]));
            Assert.That(peaks[0], Is.LessThan(2048L * 1280 * 8 * 2));
        });
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(100)]
    public void OnlyWhatARequestNeedsSetsTheDensity(int count)
    {
        ShaderGraphDescription graph = Grid(count);

        Assert.Multiple(() =>
        {
            Assert.That(graph.ClampWorkingScale(s_picture, 1, default, required: s_frame), Is.EqualTo(1));
            Assert.That(graph.ClampWorkingScale(s_picture, 1, default, required: Rect.Empty), Is.EqualTo(1), "A plan that does not know it yet.");
            Assert.That(graph.ClampWorkingScale(s_picture, 1, default), Is.LessThan(1), "The whole output.");
            Assert.That(graph.ClampWorkingScale(s_picture, 1, default, required: graph.TransformBounds(s_picture)), Is.LessThan(1), "All of it, requested.");
        });
    }

    [Test]
    public void ARegionThatDoesNotFitLowersTheDensityUntilItDoes()
    {
        ShaderGraphDescription graph = Grid(100);
        Rect region = new(-4000, -4000, 8000, 8000);

        float density = graph.ClampWorkingScale(s_picture, 1, default, required: region);

        var regions = new ShaderGraphRegions(graph, FrameOf(s_picture, density), density, default, region);
        ShaderImage output = graph.Output.RenderingImage;
        Assert.Multiple(() =>
        {
            Assert.That(density, Is.InRange(0.4f, 0.8f));
            Assert.That(ShaderGraphExecutor.EstimateImageBytes(output.DescriptionFor(regions.Frames[output.Id].PixelSize)),
                Is.LessThanOrEqualTo(ShaderGraphExecutor.MaximumTransientBytes));
        });
    }

    // The output comes from an image of half the density, but goes into a target at the working density.
    [Test]
    public void TheTargetOfTheOutputIsHeldToTheCeilingToo()
    {
        ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, static (builder, _) =>
        {
            ShaderImage output = builder.Image2D("output", builder.Source, resolutionScale: 0.5f);
            Declared(builder, builder.Source, output);
            return builder.Output(output);
        });
        Rect bounds = new(0, 0, 20000, 100);

        Assert.That(graph.ClampWorkingScale(bounds, 1, default, required: bounds), Is.EqualTo(16384f / 20000).Within(0.001));
    }

    private static IEnumerable<TestCaseData> Effects()
    {
        foreach (float scale in new[] { 0.5f, 1f, 2f })
        {
            yield return new TestCaseData(new Bloom { Radius = { Value = 6 }, Threshold = { Value = 0.25f } }, scale).SetName($"Bloom({scale})");
            yield return new TestCaseData(new Bloom { Radius = { Value = 40 }, Threshold = { Value = 0.25f }, PreserveAlpha = { Value = true } }, scale).SetName($"DownsampledBloom({scale})");
            yield return new TestCaseData(new Sharpen { Amount = { Value = 100 }, Radius = { Value = 3 } }, scale).SetName($"Sharpen({scale})");
            yield return new TestCaseData(new EdgeDetection { Thickness = { Value = 5 }, Threshold = { Value = 1 }, Original = { Value = EdgeDetectionOriginal.Behind } }, scale).SetName($"EdgeDetection({scale})");
            yield return new TestCaseData(new WaveRefraction { Amount = { Value = 4 }, Wavelength = { Value = 16 } }, scale).SetName($"WaveRefraction({scale})");
            yield return new TestCaseData(new Bokeh { Radius = { Value = 8 }, Highlight = { Value = 50 } }, scale).SetName($"Bokeh({scale})");
            yield return new TestCaseData(new Bokeh { Radius = { Value = 40 } }, scale).SetName($"DownsampledBokeh({scale})");
            yield return new TestCaseData(new RepeatEffect { Columns = { Value = 3 }, Rows = { Value = 2 }, Spacing = { Value = new Size(7, 5) }, Offset = { Value = new Point(3.5f, 1.25f) } }, scale).SetName($"Repeat({scale})");
            yield return new TestCaseData(new RadialBlur { Amount = { Value = 30 } }, scale).SetName($"ZoomBlur({scale})");
            yield return new TestCaseData(new RadialBlur { Mode = { Value = RadialBlurMode.Spin }, Amount = { Value = 30 } }, scale).SetName($"SpinBlur({scale})");
            yield return new TestCaseData(new LightRays { Length = { Value = 100 }, Threshold = { Value = 0.25f } }, scale).SetName($"LightRays({scale})");
        }
    }

    // A radial blur and light rays read between each point and their center, so a frame around the center of a wide picture
    // needs only the part of it that the frame shows.
    private static IEnumerable<TestCaseData> RadialEffects()
    {
        yield return new TestCaseData(new RadialBlur { Amount = { Value = 10 } }).SetName("{m}(ZoomBlur)");
        yield return new TestCaseData(new RadialBlur { Mode = { Value = RadialBlurMode.Spin }, Amount = { Value = 10 } }).SetName("{m}(SpinBlur)");
        yield return new TestCaseData(new LightRays()).SetName("{m}(LightRays)");
    }

    [TestCaseSource(nameof(RadialEffects))]
    public void AFrameOfAWidePictureKeepsItsDensityUnderAnEffectThatReadsTowardACenter(FilterEffect effect)
    {
        ShaderGraphDescription graph = ShaderGraphEffectTests.Record(effect);
        // 100 copies of a 4K picture across and down, and a 4K frame in their middle.
        Rect picture = new(-192_000, -108_000, 384_000, 216_000);
        Rect frame = new(-1920, -1080, 3840, 2160);

        Assert.Multiple(() =>
        {
            Assert.That(graph.ClampWorkingScale(picture, 1, default, required: frame), Is.EqualTo(1));
            Assert.That(graph.ClampWorkingScale(picture, 1, default, required: Rect.Empty), Is.EqualTo(1), "A plan that does not know it yet.");
            Assert.That(graph.ClampWorkingScale(picture, 1, default), Is.LessThan(1), "The whole output.");
        });
    }

    [TestCaseSource(nameof(Effects))]
    public void EveryEffectDrawsThePartItNeedsLikeTheWhole(FilterEffect effect, float scale)
    {
        OnDevice(_ =>
        {
            ShaderGraphDescription graph = ShaderGraphEffectTests.Record(effect);
            Rect source = new(3.25f, 6.5f, 640, 360);
            Rect bounds = graph.TransformBounds(source);
            Rect region = new(bounds.X + bounds.Width * 0.4f + 0.25f, bounds.Y + bounds.Height * 0.4f + 0.5f, 24, 18);

            var whole = Execute(graph, source, scale);
            var part = Execute(graph, source, scale, region);

            PixelRect output = PixelRect.FromRect(bounds, scale);
            PixelRect selected = PixelRect.FromRect(region, scale);
            for (int y = selected.Y; y < selected.Bottom; y++)
                for (int x = selected.X; x < selected.Right; x++)
                    for (int channel = 0; channel < 4; channel++)
                    {
                        int index = ((y - output.Y) * output.Width + x - output.X) * 4 + channel;
                        Assert.That(part.Pixels[index], Is.EqualTo(whole.Pixels[index]).Within(0.002), $"({x}, {y}), {channel}");
                    }
            Assert.That(part.Statistics.PeakResourceBytes, Is.LessThan(whole.Statistics.PeakResourceBytes));
        });
    }

    private static readonly ShaderProgram s_positions = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(binding = 0, std140) uniform Frame { vec4 logical; vec4 raster; vec4 size; } frame;
        layout(location = 0) out vec4 first;
        layout(location = 1) out vec4 second;
        void main() {
            vec2 position = frame.raster.xy + gl_FragCoord.xy / frame.size.z - frame.logical.xy;
            first = vec4(1);
            second = vec4(position / frame.logical.zw, 0, 1);
        }
        """));

    [Test]
    public void TheAttachmentsOfAPassKeepOneFrame()
    {
        OnDevice(_ =>
        {
            ShaderImage? first = null, second = null;
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, (builder, _) =>
            {
                first = builder.Image2D("first", builder.Source, TextureFormat.R16Float);
                second = builder.Image2D("second", builder.Source);
                builder.Fullscreen("both", s_positions, pass =>
                {
                    pass.ImageParameters(0, second);
                    pass.PartialImages();
                    pass.ColorAttachment(0, first);
                    pass.ColorAttachment(1, second);
                });
                return builder.Output(second);
            });
            Rect source = new(0, 0, 640, 320);
            Rect region = new(100, 50, 20, 20);

            var regions = new ShaderGraphRegions(graph, FrameOf(source), 1, default, region);
            ShaderImageFrame frame = regions.Frames[second!.Id];
            Assert.Multiple(() =>
            {
                Assert.That(regions.Frames[first!.Id], Is.EqualTo(frame));
                Assert.That(frame.Bounds, Is.EqualTo(source));
                Assert.That(frame.PixelSize.Width, Is.LessThan(640));
                Assert.That(frame.RasterBounds.Contains(region), Is.True);
            });

            float[] whole = Execute(graph, source).Pixels;
            float[] part = Execute(graph, source, region: region).Pixels;
            int index = (60 * 640 + 110) * 4;
            Assert.That(part[index], Is.EqualTo(whole[index]).Within(0.002));
            Assert.That(part[index], Is.EqualTo(110.5f / 640).Within(0.002));
        });
    }

    private static readonly ShaderProgram s_fromMetadata = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(binding = 0) uniform sampler2D image;
        layout(binding = 1, std140) uniform Source { vec4 logical; vec4 raster; vec4 size; } src;
        layout(binding = 2, std140) uniform Destination { vec4 logical; vec4 raster; vec4 size; } dst;
        layout(location = 0) out vec4 color;
        void main() {
            vec2 position = dst.raster.xy + gl_FragCoord.xy / dst.size.z;
            color = texture(image, (position - src.raster.xy) / src.raster.zw);
        }
        """));

    private static readonly ShaderProgram s_fromUv = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(location = 0) in vec2 uv;
        layout(binding = 0) uniform sampler2D image;
        layout(location = 0) out vec4 color;
        void main() { color = texture(image, uv); }
        """));

    private static void Declared(ShaderGraphBuilder builder, ShaderImage source, ShaderImage output)
        => builder.Fullscreen(output.Name, s_fromMetadata, pass =>
        {
            pass.SampledImage(0, source);
            pass.ImageParameters(1, source);
            pass.ImageParameters(2, output);
            pass.InputRegion(0, ShaderInputRegion.Identity);
            pass.PartialImages();
            pass.ColorAttachment(0, output);
        });

    private static void Undeclared(ShaderGraphBuilder builder, ShaderImage source, ShaderImage output)
        => builder.Fullscreen(output.Name, s_fromUv, pass =>
        {
            pass.SampledImage(0, source);
            pass.InputRegion(0, ShaderInputRegion.Identity);
            pass.ColorAttachment(0, output);
        });

    [Test]
    public void AnImageAPassThatDoesNotDeclareItUsesKeepsAllOfItsBounds()
    {
        OnDevice(_ =>
        {
            ShaderImage? written = null, read = null, last = null;
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, (builder, _) =>
            {
                read = builder.Image2D("read-by-a-pass-that-uses-uvs", builder.Source);
                written = builder.Image2D("written-by-a-pass-that-uses-uvs", builder.Source);
                last = builder.Image2D("last", builder.Source);
                Declared(builder, builder.Source, read);
                Undeclared(builder, read, written);
                Declared(builder, written, last);
                return builder.Output(last);
            });
            Rect source = new(0, 0, 640, 320);
            Rect region = new(100, 50, 20, 20);

            var regions = new ShaderGraphRegions(graph, FrameOf(source), 1, default, region);
            Assert.Multiple(() =>
            {
                Assert.That(regions.Frames[read!.Id].PixelSize, Is.EqualTo(new PixelSize(640, 320)));
                Assert.That(regions.Frames[written!.Id].PixelSize, Is.EqualTo(new PixelSize(640, 320)));
                Assert.That(regions.Frames[last!.Id].PixelSize.Width, Is.LessThan(640));
                Assert.That(graph.AcceptsPartialSource, Is.True, "Only passes that declare it read the source.");
            });

            float[] whole = Execute(graph, source).Pixels;
            float[] part = Execute(graph, source, region: region).Pixels;
            for (int x = 100; x < 120; x++)
            {
                int index = (60 * 640 + x) * 4;
                Assert.That(part[index], Is.EqualTo(whole[index]).Within(0.002));
            }
        });
    }

    [Test]
    public void ASourceAPassThatDoesNotDeclareItReadsIsComplete()
    {
        ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, static (builder, _) =>
        {
            ShaderImage output = builder.Image2D("output", builder.Source);
            Undeclared(builder, builder.Source, output);
            return builder.Output(output);
        });

        Assert.That(graph.AcceptsPartialSource, Is.False);
        Assert.That(graph.PartialImageSets, Is.Empty);
    }

    [TestCase(ColorEncoding.LinearStraight)]
    [TestCase(ColorEncoding.SrgbPremultiplied)]
    public void AColorConversionFollowsThePartOfTheImageItConverts(ColorEncoding encoding)
    {
        OnDevice(_ =>
        {
            ShaderImage? image = null;
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(encoding, (builder, color) =>
            {
                image = builder.Image2D("image", builder.Source);
                Declared(builder, builder.Source, image);
                return builder.Output(image, color);
            });
            Rect source = new(0, 0, 640, 320);
            Rect region = new(100, 50, 20, 20);

            var regions = new ShaderGraphRegions(graph, FrameOf(source), 1, default, region);
            Assert.That(regions.Frames[graph.Output.RenderingImage.Id], Is.EqualTo(regions.Frames[image!.Id]));
            Assert.That(regions.Frames[image.Id].PixelSize.Width, Is.LessThan(640));

            float[] whole = Execute(graph, source).Pixels;
            float[] part = Execute(graph, source, region: region).Pixels;
            for (int x = 100; x < 120; x++)
            {
                int index = (60 * 640 + x) * 4;
                Assert.That(part[index], Is.EqualTo(whole[index]).Within(0.002));
            }
        });
    }

    [Test]
    public void ASourceThatHoldsPartOfItsBoundsIsReadWhereItLies()
    {
        OnDevice(_ =>
        {
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, static (builder, _) =>
            {
                ShaderImage output = builder.Image2D("output", builder.Source);
                Declared(builder, builder.Source, output);
                return builder.Output(output);
            });
            Rect bounds = new(0, 0, 640, 320);
            Rect region = new(100, 50, 20, 20);
            var regions = new ShaderGraphRegions(graph, FrameOf(bounds), 1, default, region);
            Assert.That(graph.AcceptsPartialSource, Is.True);
            Assert.That(regions.SourceRegion.Width, Is.LessThan(40));

            // Each texel holds where it lies in the whole source.
            PixelRect held = PixelRect.FromRect(regions.SourceRegion, 1);
            var data = new Half[held.Width * held.Height * 4];
            for (int y = 0; y < held.Height; y++)
                for (int x = 0; x < held.Width; x++)
                {
                    int offset = (y * held.Width + x) * 4;
                    data[offset] = (Half)((held.X + x + 0.5f) / 640);
                    data[offset + 1] = (Half)((held.Y + y + 0.5f) / 320);
                    data[offset + 3] = (Half)1;
                }
            var context = GpuTestEnvironment.ResolveVulkan(GraphicsContextFactory.SharedContext!);
            using ITexture2D source = context.CreateTexture2D(held.Width, held.Height, TextureFormat.RGBA16Float);
            source.Upload(MemoryMarshal.AsBytes(data.AsSpan()));
            using ITexture2D output = context.CreateTexture2D(20, 20, TextureFormat.RGBA16Float);
            using ProgramCache<ShaderGraphPipeline> programs = ShaderGraphPipeline.CreateCache();

            ShaderGraphExecutor.Execute(graph, source, new(bounds, held.ToRect(1), held.Size, 1), output, 1, default,
                (device, pass, identity) => ShaderGraphPipeline.Acquire(programs, new(context, context, "test", "test"), device, pass, identity), region);

            float[] pixels = [.. MemoryMarshal.Cast<byte, Half>(output.DownloadPixels()).ToArray().Select(static value => (float)value)];
            Assert.Multiple(() =>
            {
                Assert.That(pixels[0], Is.EqualTo(100.5f / 640).Within(0.002));
                Assert.That(pixels[1], Is.EqualTo(50.5f / 320).Within(0.002));
                Assert.That(pixels[^4], Is.EqualTo(119.5f / 640).Within(0.002));
                Assert.That(pixels[^3], Is.EqualTo(69.5f / 320).Within(0.002));
            });
        });
    }

    [Test]
    public void ARegionThatMovesKeepsTakingTheSameImages()
    {
        OnDevice(_ =>
        {
            ShaderGraphDescription graph = ShaderGraphEffectTests.Record(new Sharpen { Amount = { Value = 100 }, Radius = { Value = 3 } });
            Rect source = new(0, 0, 960, 540);
            using var images = new ShaderGraphImagePool();

            int[] allocations = [.. Enumerable.Range(0, 8).Select(step => ExecuteIntoTarget(graph, source,
                region: new(100.5f + step * 7.25f, 80.25f + step * 3.5f, 300, 150), images: images).Statistics.ImageAllocations)];

            Assert.That(allocations[0], Is.GreaterThan(0));
            Assert.That(allocations.Skip(1), Is.All.Zero);
        });
    }

    [Test]
    public void AGraphWithoutDeclaredPassesAllocatesAsItDid()
    {
        OnDevice(_ =>
        {
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, static (builder, _) =>
            {
                ShaderImage output = builder.Image2D("output", builder.Source);
                Undeclared(builder, builder.Source, output);
                return builder.Output(output);
            });
            Rect source = new(0, 0, 640, 320);

            var whole = Execute(graph, source);
            var part = Execute(graph, source, region: new(100, 50, 20, 20));

            Assert.That(part.Statistics.PeakResourceBytes, Is.EqualTo(whole.Statistics.PeakResourceBytes));
            Assert.That(graph.ClampWorkingScale(source, 1, default, required: Rect.Empty), Is.EqualTo(graph.ClampWorkingScale(source, 1, default)));
        });
    }
}
