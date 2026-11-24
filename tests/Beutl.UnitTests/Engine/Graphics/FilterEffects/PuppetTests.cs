using System.Diagnostics;
using System.Numerics;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Serialization;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.TestInfrastructure;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class PuppetTests
{
    [Test]
    public void MeshCoversTheBandWhenItsScanlineMeetsAVertexInAnotherStrip()
    {
        // The left contour turns at y=36, halfway through the band between y=24 and y=48.
        Point[][] contours = [[new(0, 0), new(120, 0), new(120, 96), new(0, 96), new(3, 36)]];
        var mesh = PuppetMesh.Create(contours, new Size(120, 96), new PixelSize(120, 96), [], out _)!;

        Assert.That(mesh.TryMapToRest(mesh.Rest, new Vector2(60, 36), out _), Is.True);
    }

    [Test]
    public void MeshSharesStripEdgesAndPinsAroundAConcaveOutlineWithAHole()
    {
        // The sharp bend on the left is outside the next strip, which still fills to its seam.
        Point[] outline =
        [
            new(0, 0), new(120, 0), new(120, 48), new(90, 48), new(90, 96),
            new(2, 96), new(2, 19), new(18, 14)
        ];
        Point[] hole = [new(61, 12), new(79, 28), new(79, 65), new(56, 73)];
        Point[][] contours = [outline, hole];
        var size = new Size(120, 96);
        // The first pin is exactly on a strip seam. The last pin is inside the hole.
        Point[] anchors = [new(0.4f, 0.0625f), new(0.6f, 0.875f), new(0.5f, 0.5f)];
        PuppetMesh? mesh = PuppetMesh.Create(contours, size, new PixelSize(120, 96), anchors, out bool exceeded);

        Assert.That(exceeded, Is.False);
        Assert.That(mesh, Is.Not.Null);
        Assert.That(mesh!.PinVertices[0], Is.GreaterThanOrEqualTo(0));
        Assert.That(mesh.PinVertices[1], Is.GreaterThanOrEqualTo(0));
        Assert.That(mesh.PinVertices[2], Is.EqualTo(-1));
        Assert.That(mesh.Rest[mesh.PinVertices[0]], Is.EqualTo(PuppetMesh.ToLocal(size, anchors[0])));
        Assert.That(mesh.Rest[mesh.PinVertices[1]], Is.EqualTo(PuppetMesh.ToLocal(size, anchors[1])));

        double doubleArea = 0;
        var edgeCounts = new Dictionary<(int A, int B), int>();
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            int a = mesh.Indices[i], b = mesh.Indices[i + 1], c = mesh.Indices[i + 2];
            doubleArea += CrossProduct(mesh.Rest[b] - mesh.Rest[a], mesh.Rest[c] - mesh.Rest[a]);
            CountEdge(a, b);
            CountEdge(b, c);
            CountEdge(c, a);
        }

        Assert.That(doubleArea, Is.EqualTo(ContourDoubleArea(outline) - ContourDoubleArea(hole)).Within(0.05));
        Assert.That(edgeCounts.Values, Has.All.InRange(1, 2));
        foreach (var (edge, count) in edgeCounts)
        {
            if (count == 1)
            {
                Vector2 midpoint = (mesh.Rest[edge.A] + mesh.Rest[edge.B]) * 0.5f;
                Assert.That(contours.Any(contour => OnContour(contour, midpoint)), Is.True,
                    $"A shared edge was split on only one side at {midpoint}.");
            }
        }

        void CountEdge(int a, int b)
        {
            var key = (Math.Min(a, b), Math.Max(a, b));
            edgeCounts[key] = edgeCounts.GetValueOrDefault(key) + 1;
        }

        static double ContourDoubleArea(Point[] contour)
        {
            double area = 0;
            for (int i = 0; i < contour.Length; i++)
                area += CrossProduct(ToVector(contour[i]), ToVector(contour[(i + 1) % contour.Length]));
            return Math.Abs(area);
        }

        static bool OnContour(Point[] contour, Vector2 point)
        {
            for (int i = 0; i < contour.Length; i++)
            {
                Vector2 start = ToVector(contour[i]);
                Vector2 direction = ToVector(contour[(i + 1) % contour.Length]) - start;
                Vector2 offset = point - start;
                if (Math.Abs(CrossProduct(direction, offset)) <= 0.005
                    && Vector2.Dot(offset, direction) >= 0
                    && Vector2.Dot(offset, direction) <= direction.LengthSquared())
                    return true;
            }
            return false;
        }

        static Vector2 ToVector(Point point) => new(point.X, point.Y);
        static double CrossProduct(Vector2 a, Vector2 b) => (double)a.X * b.Y - (double)a.Y * b.X;
    }

    [Test]
    public void HolesAndNestedIslandsHitTestAndDeformIndependently()
    {
        var size = new Size(180, 120);
        using RenderTarget input = CpuRenderTargets.Create(180, 120);
        SKCanvas canvas = input.Value.Canvas;
        canvas.Clear(SKColors.Transparent);
        Fill(canvas, new SKRect(8, 8, 119, 111), SKColors.Red);
        using (var clear = new SKPaint { BlendMode = SKBlendMode.Clear })
            canvas.DrawRect(new SKRect(35, 40, 80, 85), clear);
        Fill(canvas, new SKRect(48, 53, 63, 68), new SKColor(30, 210, 255, 128));
        Fill(canvas, new SKRect(144, 35, 171, 75), SKColors.Blue);
        using var target = new EffectTarget(input, new Rect(size));
        Point[] anchors = [new(0.09f, 0.22f), new(0.55f, 0.79f)];
        var (mesh, deformer) = new PuppetMeshCache().GetOrCreate(target, anchors)!.Value;
        Point[] destinations = [anchors[0], new(0.68f, 0.83f)];
        Vector2[] warped = deformer.Deform(size, destinations);

        Assert.Multiple(() =>
        {
            Assert.That(mesh.TryMapToRest(mesh.Rest, new Vector2(40, 45), out _), Is.False, "hole");
            Assert.That(mesh.TryMapToRest(mesh.Rest, new Vector2(52, 58), out _), Is.True, "island in hole");
            Assert.That(mesh.TryMapToRest(mesh.Rest, new Vector2(155, 50), out _), Is.True, "separate island");
            Assert.That(warped[mesh.PinVertices[0]], Is.EqualTo(PuppetMesh.ToLocal(size, destinations[0])));
            Assert.That(warped[mesh.PinVertices[1]], Is.EqualTo(PuppetMesh.ToLocal(size, destinations[1])));
            Assert.That(Enumerable.Range(0, mesh.Rest.Length).Count(index => warped[index] != mesh.Rest[index]), Is.GreaterThan(2));
            foreach (int index in Enumerable.Range(0, mesh.Rest.Length))
            {
                Vector2 rest = mesh.Rest[index];
                if (rest.X >= 140 || (rest.X is >= 47 and <= 64 && rest.Y is >= 52 and <= 69))
                    Assert.That(warped[index], Is.EqualTo(rest));
            }
            Assert.That(deformer.Deform(size, anchors), Is.SameAs(mesh.Rest));
            Assert.That(deformer.Deform(size, destinations), Is.EqualTo(warped));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TranslatingTracedEllipseKeepsItsOpaqueInterior(bool vulkan)
    {
        if (vulkan)
        {
            VulkanTestEnvironment.EnsureAvailable();
            VulkanTestEnvironment.InvokeOnRenderThread(() =>
                Verify((width, height) => RenderTarget.Create(width, height)!));
        }
        else
        {
            Verify(CpuRenderTargets.Create);
        }

        static void Verify(Func<int, int, RenderTarget> createTarget)
        {
            var bounds = new Rect(20.25f, -7.75f, 320, 240);
            var footprint = PixelRect.FromRect(bounds, 1);
            using RenderTarget backing = createTarget(footprint.Width, footprint.Height);
            var oval = new SKRect(6, 6, footprint.Width - 6, footprint.Height - 6);
            SKCanvas canvas = backing.Value.Canvas;
            canvas.Clear(SKColors.Transparent);
            using (var paint = new SKPaint { Color = SKColors.OrangeRed, IsAntialias = true })
                canvas.DrawOval(oval, paint);

            var shift = new Point(0.021f, 0.047f);
            var effect = new Puppet();
            foreach (Point rest in (Point[])[new(0.22f, 0.26f), new(0.42f, 0.5f), new(0.72f, 0.4f), new(0.27f, 0.72f)])
                effect.Pins.Add(Pin(rest, rest + shift));

            var source = new EffectTarget(backing, bounds, EffectiveScale.At(1), footprint);
            Rect raster = source.Placement.RasterBounds;
            using var targets = new EffectTargets { source };
            using var resource = effect.ToResource(CompositionContext.Default);
            using var context = new FilterEffectContext(bounds);
            context.ApplyTransactional(resource);
            using var builder = new SKImageFilterBuilder();
            using var executor = new FilterEffectExecutor(targets, builder, RenderIntent.Preview, RenderRequestPurpose.Frame,
                drawableBrushMaterializer: null, workingScale: 1, maxWorkingScale: 1);
            executor.Apply(context);
            executor.Flush(false);

            EffectTarget output = targets.Single();
            using Bitmap alpha = output.RenderTarget!.SnapshotAlpha();
            var center = new Point(raster.X + oval.MidX + shift.X * bounds.Width,
                raster.Y + oval.MidY + shift.Y * bounds.Height);
            Rect outputRaster = output.Placement.RasterBounds;
            int tested = 0;
            int missing = 0;
            for (int y = 0; y < alpha.Height; y++)
            {
                float dy = (outputRaster.Y + y + 0.5f - center.Y) / (oval.Height * 0.5f);
                Span<byte> row = alpha.GetRow(y);
                for (int x = 0; x < alpha.Width; x++)
                {
                    float dx = (outputRaster.X + x + 0.5f - center.X) / (oval.Width * 0.5f);
                    if (dx * dx + dy * dy >= 0.8f) continue;

                    tested++;
                    if (row[x] < 250) missing++;
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(tested, Is.GreaterThan(40_000));
                Assert.That(missing, Is.Zero);
            });
        }
    }

    [TestCase(1f, RenderIntent.Preview)]
    [TestCase(2f, RenderIntent.Delivery)]
    public void SplitInputTranslatesTogetherAndPreservesTheHole(float scale, RenderIntent intent)
    {
        var bounds = new Rect(20, 30, 120, 80);
        var left = new Rect(20, 30, 60, 80);
        var right = new Rect(80, 30, 60, 80);
        using var targets = new EffectTargets
        {
            MakeTarget(left, scale, SKColors.Red),
            MakeTarget(right, scale, SKColors.Blue),
        };
        var effect = new Puppet { Pins = { Pin(new Point(0.25f, 0.25f), new Point(0.50f, 0.5f)) } };
        using var resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(bounds);
        context.ApplyTransactional(resource);
        using var builder = new SKImageFilterBuilder();
        using var executor = new FilterEffectExecutor(targets, builder, intent, RenderRequestPurpose.Frame,
            drawableBrushMaterializer: null, workingScale: scale, maxWorkingScale: scale);
        executor.Apply(context);
        executor.Flush(false);

        EffectTarget output = targets.Single();
        using Bitmap alpha = output.RenderTarget!.SnapshotAlpha();
        Assert.Multiple(() =>
        {
            Assert.That(output.Bounds.X, Is.EqualTo(50).Within(1));
            Assert.That(output.Bounds.Y, Is.EqualTo(50).Within(1));
            Assert.That(AlphaAt(new Point(109.5f, 69)), Is.EqualTo(255), "left of the former seam");
            Assert.That(AlphaAt(new Point(110.5f, 69)), Is.EqualTo(255), "right of the former seam");
            Assert.That(AlphaAt(new Point(93, 90)), Is.Zero, "hole moved with the connected component");
            Assert.That(AlphaAt(new Point(70, 90)), Is.InRange(100, 155), "translucent source texture");
        });

        byte AlphaAt(Point point)
        {
            Point local = (point - output.Placement.RasterBounds.Position) * output.Placement.Scale.Value;
            return alpha.GetRow((int)local.Y)[(int)local.X];
        }

        static EffectTarget MakeTarget(Rect tile, float density, SKColor color)
        {
            var footprint = PixelRect.FromRect(tile, density);
            using RenderTarget backing = CpuRenderTargets.Create(footprint.Width, footprint.Height);
            SKCanvas canvas = backing.Value.Canvas;
            canvas.Clear(color.WithAlpha(128));
            Fill(canvas, new SKRect(0, 0, footprint.Width, 30 * density), color);
            if (color == SKColors.Red)
            {
                using var clear = new SKPaint { BlendMode = SKBlendMode.Clear };
                canvas.DrawRect(36 * density, 32 * density, 14 * density, 16 * density, clear);
            }
            return new EffectTarget(backing, tile, EffectiveScale.At(density), footprint);
        }
    }

    [Test]
    public void PinIdsRestPositionsAndIndependentKeysRoundTrip()
    {
        var effect = new Puppet
        {
            Pins = { Pin(new Point(.2f, .4f), new Point(.25f, .4f)), Pin(new Point(.7f, .6f), new Point(.8f, .6f)) }
        };
        PuppetPin animated = effect.Pins[1];
        animated.Position.SetKeyFrameAnimation(new KeyFrameAnimation<Point>
        {
            KeyFrames = { new KeyFrame<Point> { KeyTime = { Value = MediaTime.FromSeconds(1) }, Value = { Value = new Point(1.2f, .6f) } } }
        });
        var restored = (Puppet)CoreSerializer.DeserializeFromJsonObject(CoreSerializer.SerializeToJsonObject(effect), typeof(Puppet));
        Assert.Multiple(() =>
        {
            Assert.That(restored.Pins.Select(pin => pin.Id.Value), Is.EqualTo(effect.Pins.Select(pin => pin.Id.Value)));
            Assert.That(restored.Pins[0].Position.Value, Is.EqualTo(effect.Pins[0].Position.Value));
            Assert.That(restored.Pins[0].Position.GetKeyFrameAnimation(), Is.Null);
            Assert.That(restored.Pins[1].RestPosition.Value, Is.EqualTo(animated.RestPosition.Value));
            Assert.That(restored.Pins[1].Position.GetKeyFrameAnimation()!.KeyFrames.Single().Value,
                Is.EqualTo(new Point(1.2f, .6f)));
        });
    }

    [Test]
    public void AddingOnDeformedImageAttachesToTheUndeformedTexture()
    {
        var size = new Size(100, 100);
        using RenderTarget input = CpuRenderTargets.Create(100, 100);
        input.Value.Canvas.Clear(SKColors.Orange);
        var bounds = new Rect(20, 30, size.Width, size.Height);
        using var target = new EffectTarget(input, bounds);
        Point[] rest = [new(0.5f, 0.25f)];
        var (mesh, deformer) = new PuppetMeshCache().GetOrCreate(target, rest)!.Value;
        Vector2[] deformed = deformer.Deform(size, [new Point(0.7f, 0.05f)]);
        var placement = new Puppet.PinPlacement(bounds, mesh, deformed);

        PuppetPin pin = placement.CreatePin(new Point(80, 60))!;
        Assert.Multiple(() =>
        {
            Assert.That(pin.RestPosition.Value.X, Is.EqualTo(0.4f).Within(0.001));
            Assert.That(pin.RestPosition.Value.Y, Is.EqualTo(0.5f).Within(0.001));
            Assert.That(pin.Position.Value, Is.EqualTo(new Point(0.6f, 0.3f)));
            Assert.That(placement.CreatePin(new Point(90, 35)), Is.Null, "existing pin at its displaced position");
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AnalysisMaskUsesSemanticBoundsWithFractionalOriginAndRasterApron(bool vulkan)
    {
        if (vulkan)
        {
            VulkanTestEnvironment.EnsureAvailable();
            VulkanTestEnvironment.InvokeOnRenderThread(() =>
                Verify((width, height) => RenderTarget.Create(width, height)!));
        }
        else
        {
            Verify(CpuRenderTargets.Create);
        }

        static void Verify(Func<int, int, RenderTarget> createTarget)
        {
            var bounds = new Rect(13.25f, -4.25f, 80, 60);
            const float density = 2;
            PixelRect pixels = PixelRect.FromRect(bounds, density);
            const int apron = 6;
            var footprint = new PixelRect(pixels.X - apron, pixels.Y - apron,
                pixels.Width + apron * 2, pixels.Height + apron * 2);
            using RenderTarget input = createTarget(footprint.Width, footprint.Height);
            using var target = new EffectTarget(input, bounds, EffectiveScale.At(density), footprint);
            Point origin = (bounds.Position - target.Placement.RasterBounds.Position) * density;
            SKCanvas canvas = input.Value.Canvas;
            canvas.Clear(SKColors.Transparent);
            Fill(canvas, new SKRect(0, 0, 3, footprint.Height), SKColors.Magenta);
            Fill(canvas, InRaster(6, 12, 26, 35), new SKColor(30, 210, 255, 80));
            using (var clear = new SKPaint { BlendMode = SKBlendMode.Clear })
                canvas.DrawRect(InRaster(13, 19, 19, 27), clear);

            var (mesh, _) = new PuppetMeshCache().GetOrCreate(target, [])!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(mesh.Rest.Min(vertex => vertex.X), Is.EqualTo(6).Within(0.75));
                Assert.That(mesh.Rest.Max(vertex => vertex.X), Is.EqualTo(26).Within(0.75));
                Assert.That(mesh.Rest.Min(vertex => vertex.Y), Is.EqualTo(12).Within(0.75));
                Assert.That(mesh.Rest.Max(vertex => vertex.Y), Is.EqualTo(35).Within(0.75));
                Assert.That(mesh.TryMapToRest(mesh.Rest, new Vector2(8, 14), out _), Is.True);
                Assert.That(mesh.TryMapToRest(mesh.Rest, new Vector2(16, 23), out _), Is.False, "hole");
            });

            SKRect InRaster(float left, float top, float right, float bottom)
                => new(origin.X + left * density, origin.Y + top * density,
                    origin.X + right * density, origin.Y + bottom * density);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Prepared1080pMeshSurvivesPositionChangesAndTracksAlphaChanges(bool vulkan)
    {
        if (vulkan)
        {
            VulkanTestEnvironment.EnsureAvailable();
            VulkanTestEnvironment.InvokeOnRenderThread(() => MeasureMeshReuse(
                (width, height) => RenderTarget.Create(width, height)!, "Vulkan"));
        }
        else
        {
            MeasureMeshReuse(CpuRenderTargets.Create, "CPU");
        }
    }

    private static void MeasureMeshReuse(Func<int, int, RenderTarget> createTarget, string backend)
    {
        const int width = 1920;
        const int height = 1080;
        var size = new Size(width, height);
        using RenderTarget input = createTarget(width, height);
        SKCanvas canvas = input.Value.Canvas;
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = SKColors.OrangeRed, IsAntialias = true };
        canvas.DrawOval(new SKRect(100, 120, 1800, 970), paint);
        Point[] anchors = [new(.2f, .5f), new(.5f, .2f), new(.8f, .55f)];
        using var target = new EffectTarget(input, new Rect(size));
        var cache = new PuppetMeshCache();

        var watch = Stopwatch.StartNew();
        var first = cache.GetOrCreate(target, anchors)!.Value;
        TimeSpan build = watch.Elapsed;
        first.Deformer.Deform(size, [anchors[0], anchors[1], new(.8f, .66f)]);
        var frameTimes = new List<double>();
        for (int i = 0; i < 16; i++)
        {
            Point[] destinations = [anchors[0], anchors[1], new(.8f + i * .002f, .66f)];
            watch.Restart();
            var next = cache.GetOrCreate(target, anchors)!.Value;
            Vector2[] warped = next.Deformer.Deform(size, destinations);
            frameTimes.Add(watch.Elapsed.TotalMilliseconds);
            Assert.That(next.Mesh, Is.SameAs(first.Mesh));
            Assert.That(warped[next.Mesh.PinVertices[2]], Is.EqualTo(PuppetMesh.ToLocal(size, destinations[2])));
        }

        using (var clear = new SKPaint { BlendMode = SKBlendMode.Clear })
            canvas.DrawRect(new SKRect(870, 430, 1100, 660), clear);
        Assert.That(cache.GetOrCreate(target, anchors)!.Value.Mesh, Is.Not.SameAs(first.Mesh));
        frameTimes.Sort();
        TestContext.Progress.WriteLine($"Puppet 1080p {backend}, {first.Mesh.Rest.Length} vertices, first mesh {build.TotalMilliseconds:F1} ms,"
            + $" warmed mask and solve p95 {frameTimes[^1]:F1} ms");
    }

    private static PuppetPin Pin(Point rest, Point position)
        => new() { RestPosition = { Value = rest }, Position = { Value = position } };

    private static void Fill(SKCanvas canvas, SKRect bounds, SKColor color)
    {
        using var paint = new SKPaint { Color = color };
        canvas.DrawRect(bounds, paint);
    }
}
