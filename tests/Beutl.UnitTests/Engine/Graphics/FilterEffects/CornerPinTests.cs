using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Serialization;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;
using Beutl.UnitTests.TestInfrastructure;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class CornerPinTests
{
    private static readonly Rect s_input = new(10, 20, 100, 80);
    private static readonly Rgba s_red = new(1, 0, 0, 1);
    private static readonly Rgba s_green = new(0, 1, 0, 1);
    private static readonly Rgba s_blue = new(0, 0, 1, 1);
    private static readonly Rgba s_yellow = new(1, 1, 0, 1);

    private static IEnumerable<TestCaseData> Quadrilaterals()
    {
        yield return Quad("Identity", [new(10, 20), new(110, 20), new(110, 100), new(10, 100)], new(60, 60));
        yield return Quad("Translation", [new(-4, 28), new(96, 28), new(96, 108), new(-4, 108)], new(46, 68));
        yield return Quad("Scale", [new(10, 20), new(210, 20), new(210, 60), new(10, 60)], new(110, 40));
        yield return Quad("Perspective", [new(35, 20), new(85, 20), new(110, 100), new(10, 100)], new(60, 140f / 3));
        yield return Quad("HorizontalFlip", [new(110, 20), new(10, 20), new(10, 100), new(110, 100)], new(60, 60));
        yield return Quad("VerticalFlip", [new(10, 100), new(110, 100), new(110, 20), new(10, 20)], new(60, 60));

        static TestCaseData Quad(string name, Point[] corners, Point center) => new TestCaseData(corners, center).SetArgDisplayNames(name);
    }

    [TestCaseSource(nameof(Quadrilaterals))]
    public void HomographyMapsAnOffsetInputToTheQuad(Point[] corners, Point center)
    {
        Assert.That(CornerPin.TryCreateHomography(s_input, corners[0], corners[1], corners[2], corners[3], out Matrix matrix), Is.True);
        Point[] inputCorners = [s_input.TopLeft, s_input.TopRight, s_input.BottomRight, s_input.BottomLeft];
        using (Assert.EnterMultipleScope())
        {
            for (int i = 0; i < inputCorners.Length; i++)
                AssertPoint(matrix.Transform(inputCorners[i]), corners[i]);
            AssertPoint(matrix.Transform(s_input.Center), center);
        }
    }

    [Test]
    public void HomographyRejectsAStoredDivisorThatRoundsToZero()
    {
        var input = new Rect(8192, 0, 2, 16);
        Assert.That(CornerPin.TryCreateHomography(input, new Point(128, 0), new Point(0.015625f, 0),
            new Point(0.015625f, 0.015625f), new Point(128, 128), out _), Is.False);
    }

    private static IEnumerable<TestCaseData> InvalidQuads()
    {
        yield return Quad("Concave", [new(0.6f, 0.7f), new(1, 0), new(1, 1), new(0, 1)]);
        yield return Quad("Bowtie", [new(0, 0), new(1, 1), new(1, 0), new(0, 1)]);
        yield return Quad("Collinear", [new(0, 0.5f), new(1, 0.5f), new(0.75f, 0.5f), new(0.25f, 0.5f)]);
        yield return Quad("ThreeCollinear", [new(0.5f, 0.5f), new(1, 0), new(1, 1), new(0, 1)]);
        yield return Quad("NearlyCollinearTopLeft", [new(0.5f - 1e-6f, 0.5f - 1e-6f), new(1, 0), new(1, 1), new(0, 1)]);
        yield return Quad("NearlyCollinearBottomRight", [new(0, 0), new(1, 0), new(0.5f + 1e-6f, 0.5f + 1e-6f), new(0, 1)]);
        yield return Quad("Duplicate", [new(0, 0), new(0, 0), new(1, 1), new(0, 1)]);
        yield return Quad("NaN", [new(float.NaN, 0), new(1, 0), new(1, 1), new(0, 1)]);
        yield return Quad("Infinity", [new(0, 0), new(1, float.PositiveInfinity), new(1, 1), new(0, 1)]);

        static TestCaseData Quad(string name, Point[] points) => new TestCaseData(points).SetArgDisplayNames(name);
    }

    [TestCaseSource(nameof(InvalidQuads))]
    public void InvalidQuadHasNoBoundsOrTargets(Point[] corners)
    {
        CornerPin effect = Create(corners[0], corners[1], corners[2], corners[3]);
        using FilterEffectContext context = Record(effect, s_input);
        using var targets = new EffectTargets { CreateSource(s_input) };
        Execute(context, targets, intent: RenderIntent.Delivery);
        Assert.That(context.Bounds, Is.EqualTo(Rect.Empty));
        Assert.That(targets, Is.Empty);
    }

    [Test]
    public void BoundsStaySymbolicOrEmptyUntilThereIsInput()
    {
        var effect = Trapezoid();
        using FilterEffectContext symbolic = Record(effect, Rect.Invalid);
        using FilterEffectContext empty = Record(effect, new Rect(10, 20, 100, 0));
        Assert.That(symbolic.Bounds.IsInvalid, Is.True);
        Assert.That(empty.Bounds, Is.EqualTo(Rect.Empty));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void IdentityReusesTheOriginalTarget(bool absolute)
    {
        var bounds = new Rect(-6.25f, 10.75f, 60, 40);
        var effect = new CornerPin();
        if (absolute)
        {
            effect.TopLeft.Value = Absolute(0, 0);
            effect.TopRight.Value = Absolute(60, 0);
            effect.BottomRight.Value = Absolute(60, 40);
            effect.BottomLeft.Value = Absolute(0, 40);
        }

        EffectTarget target = CreateSource(bounds, scale: 2, apron: 4);
        using var targets = new EffectTargets { target };
        using FilterEffectContext context = Record(effect, bounds);
        Execute(context, targets);
        Assert.That(context.Bounds, Is.EqualTo(bounds));
        Assert.That(targets.Single(), Is.SameAs(target));
        if (!absolute)
            Assert.That(context.CountItems(), Is.Zero);
    }

    [Test]
    public void TrapezoidSamplesInPerspectiveAndAntialiasesTheOutline()
    {
        using EffectTargets targets = Render(Trapezoid(), s_input);
        EffectTarget output = targets.Single();
        using Bitmap bitmap = output.RenderTarget!.Snapshot();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(output.Bounds, Is.EqualTo(s_input));
            Assert.That(Pixel(bitmap, output, 45, 35), Is.EqualTo(s_red));
            Assert.That(Pixel(bitmap, output, 75, 35), Is.EqualTo(s_green));
            // Perspective puts the horizontal quadrant boundary at y = 46.67, before the affine midpoint at y = 60.
            Assert.That(Pixel(bitmap, output, 45, 53), Is.EqualTo(s_blue));
            Assert.That(Pixel(bitmap, output, 75, 53), Is.EqualTo(s_yellow));
            Assert.That(Pixel(bitmap, output, 14, 35).Alpha, Is.Zero);
            Assert.That(Enumerable.Range(25, 12).Select(x => Pixel(bitmap, output, x, 35).Alpha),
                Has.Some.InRange(0.05f, 0.95f));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FlipsKeepAllQuadrants(bool vertical)
    {
        CornerPin effect = vertical
            ? Create(new(0, 1), new(1, 1), new(1, 0), new(0, 0))
            : Create(new(1, 0), new(0, 0), new(0, 1), new(1, 1));
        using EffectTargets targets = Render(effect, s_input);
        EffectTarget output = targets.Single();
        using Bitmap bitmap = output.RenderTarget!.Snapshot();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(output.Bounds, Is.EqualTo(s_input));
            Assert.That(Pixel(bitmap, output, 25, 30), Is.EqualTo(vertical ? s_blue : s_green));
            Assert.That(Pixel(bitmap, output, 95, 30), Is.EqualTo(vertical ? s_yellow : s_red));
            Assert.That(Pixel(bitmap, output, 25, 90), Is.EqualTo(vertical ? s_red : s_yellow));
            Assert.That(Pixel(bitmap, output, 95, 90), Is.EqualTo(vertical ? s_green : s_blue));
        }
    }

    [TestCase(1f)]
    [TestCase(1.5f)]
    [TestCase(2f)]
    public void FractionalOutputCoversTheTightBoundsAtEachScale(float workingScale)
    {
        var input = new Rect(10.25f, 20.25f, 40, 32);
        var effect = new CornerPin
        {
            TopLeft = { Value = Absolute(5, -4) },
            TopRight = { Value = Absolute(45.5f, -4) },
            BottomRight = { Value = Absolute(45.5f, 28.5f) },
            BottomLeft = { Value = Absolute(5, 28.5f) },
        };
        var expected = new Rect(15.25f, 16.25f, 40.5f, 32.5f);
        using FilterEffectContext context = Record(effect, input);
        using EffectTargets targets = Render(effect, input, sourceScale: 2, workingScale: workingScale, apron: 6);
        EffectTarget output = targets.Single();
        using Bitmap bitmap = output.RenderTarget!.Snapshot();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Bounds, Is.EqualTo(expected));
            Assert.That(output.Bounds, Is.EqualTo(expected));
            Assert.That(output.Placement.DeviceBounds, Is.EqualTo(PixelRect.FromRect(expected, workingScale)));
            Assert.That(Pixel(bitmap, output, 17, 18), Is.EqualTo(s_red));
            Assert.That(Pixel(bitmap, output, 54, 18), Is.EqualTo(s_green));
            Assert.That(Pixel(bitmap, output, 17, 47), Is.EqualTo(s_blue));
            Assert.That(Pixel(bitmap, output, 54, 47), Is.EqualTo(s_yellow));
            Assert.That(ImageMetrics.ReadPixel(bitmap, bitmap.Width - 1, bitmap.Height - 1).Alpha, Is.GreaterThan(0.05f));
        }
    }

    [Test]
    public void ForcedFlushKeepsFractionalCornerPinEdgePixels()
    {
        var input = new Rect(0, 0, 50, 32);
        var expected = new Rect(0.75f, 0.75f, 50, 32);
        using FilterEffectContext context = Record(CreateAbsolute(expected), input);
        using var targets = new EffectTargets { CreateSource(input) };
        using var builder = new SKImageFilterBuilder();
        using var executor = new FilterEffectExecutor(targets, builder, RenderIntent.Delivery, RenderRequestPurpose.Auxiliary,
            drawableBrushMaterializer: null);
        executor.Apply(context);

        EffectTarget warped = targets.Single();
        Rect rasterBounds = warped.Placement.RasterBounds;
        using Bitmap before = warped.RenderTarget!.Snapshot();
        Assert.That(ImageMetrics.ReadPixel(before, 50, 15).Alpha, Is.GreaterThan(0.5f));

        for (int i = 0; i < 2; i++)
        {
            executor.Flush();
            EffectTarget rebuilt = targets.Single();
            Assert.That(rebuilt.Bounds, Is.EqualTo(expected));
            Assert.That(rebuilt.Placement.DeviceBounds, Is.EqualTo(PixelRect.FromRect(expected, 1)));
            Assert.That(rebuilt.Placement.RasterBounds, Is.EqualTo(rasterBounds));
            using Bitmap after = rebuilt.RenderTarget!.Snapshot();
            Assert.That(after.GetPixelSpan<ushort>().ToArray(), Is.EqualTo(before.GetPixelSpan<ushort>().ToArray()));
        }
    }

    [Test]
    public void MagnificationKeepsEdgesSharpAndDoesNotSampleTheSourceApron()
    {
        var input = new Rect(10, 20, 32, 24);
        CornerPin effect = Create(new(0, 0), new(4, 0), new(4, 4), new(0, 4));
        using EffectTargets targets = Render(effect, input, apron: 6);
        EffectTarget output = targets.Single();
        using Bitmap bitmap = output.RenderTarget!.Snapshot();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Pixel(bitmap, output, 10, 20), Is.EqualTo(s_red));
            Assert.That(Pixel(bitmap, output, 137, 20), Is.EqualTo(s_green));
            Assert.That(Pixel(bitmap, output, 10, 115), Is.EqualTo(s_blue));
            Assert.That(Pixel(bitmap, output, 137, 115), Is.EqualTo(s_yellow));
            Assert.That(Pixel(bitmap, output, 71, 30), Is.EqualTo(s_red));
            Assert.That(Pixel(bitmap, output, 76, 30), Is.EqualTo(s_green));
            Assert.That(Pixel(bitmap, output, 73, 30).Green, Is.InRange(0.2f, 0.8f));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MinifyingInPerspectiveFiltersHighFrequencyDetail(bool gpu)
    {
        if (gpu)
        {
            TestGpu.RequireAnyDevice();
            RenderThread.Dispatcher.Invoke(() => AssertMinification(gpu: true));
        }
        else
        {
            AssertMinification(gpu: false);
        }
    }

    [Test]
    public void StrongMinificationStillDrawsAValidQuad()
    {
        var input = new Rect(0, 0, 3840, 2160);
        var expected = new Rect(0, 0, 4, 2);
        CornerPin effect = CreateAbsolute(expected);
        using FilterEffectContext context = Record(effect, input);
        using var targets = new EffectTargets { CreateSource(input) };
        Execute(context, targets);

        Assert.That(context.Bounds, Is.EqualTo(expected));
        EffectTarget output = targets.Single();
        using Bitmap bitmap = output.RenderTarget!.Snapshot();
        Assert.That(output.Bounds, Is.EqualTo(expected));
        Assert.That(ImageMetrics.ReadPixel(bitmap, 1, 1).Alpha, Is.EqualTo(1f));
    }

    private static void AssertMinification(bool gpu)
    {
        var input = new Rect(0, 0, 128, 128);
        var effect = new CornerPin
        {
            TopLeft = { Value = Absolute(8, 5) },
            TopRight = { Value = Absolute(24, 6) },
            BottomRight = { Value = Absolute(29, 31) },
            BottomLeft = { Value = Absolute(2, 32) },
        };
        using var targets = new EffectTargets
        {
            CreateSource(input, draw: (canvas, area) =>
            {
                canvas.Clear(SKColors.Black);
                using var paint = new SKPaint { Color = SKColors.White };
                for (int y = 0; y < area.Height; y++)
                    for (int x = y % 2; x < area.Width; x += 2)
                        canvas.DrawRect(x, y, 1, 1, paint);
            }, gpu: gpu),
        };
        using FilterEffectContext context = Record(effect, input);
        Execute(context, targets);
        EffectTarget output = targets.Single();
        using Bitmap bitmap = output.RenderTarget!.Snapshot();
        float[] levels = (from y in Enumerable.Range(12, 12)
                          from x in Enumerable.Range(11, 10)
                          select Pixel(bitmap, output, x, y).Red).ToArray();
        Assert.That(levels, Has.All.InRange(0.35f, 0.65f));
    }

    [Test]
    public void SplitTilesUseTheSamePerspectiveAsTheHandles()
    {
        var input = new Rect(0, 0, 100, 80);
        var effect = Trapezoid();
        var group = new FilterEffectGroup { Children = { new SplitEffect(), effect } };
        using EffectTargets targets = Render(group, input);
        Assert.That(targets, Has.Count.EqualTo(4));

        IReadOnlyList<EditingHandle> handles = effect.GetEditingHandles(MediaTime.Zero, input);
        using Bitmap bitmap = CompositeTiles(targets, new PixelSize(100, 80));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(handles.Select(x => x.Position), Is.EqualTo(new Point[] { new(25, 0), new(75, 0), new(100, 80), new(0, 80) }));
            Assert.That(ImageMetrics.ReadPixel(bitmap, 35, 15), Is.EqualTo(s_red));
            Assert.That(ImageMetrics.ReadPixel(bitmap, 65, 15), Is.EqualTo(s_green));
            Assert.That(ImageMetrics.ReadPixel(bitmap, 35, 33), Is.EqualTo(s_blue));
            Assert.That(ImageMetrics.ReadPixel(bitmap, 65, 33), Is.EqualTo(s_yellow));
            Assert.That(ImageMetrics.ReadPixel(bitmap, 10, 15).Alpha, Is.Zero);
            Assert.That(Enumerable.Range(25, 5).Select(y => ImageMetrics.ReadPixel(bitmap, 49, y).Alpha),
                Has.All.GreaterThan(0.98f));
        }
    }

    [Test]
    public void SeveralTilesMeetingAlongAnEdgeKeepItOpaque()
    {
        var input = new Rect(0, 0, 100, 80);
        using var targets = new EffectTargets
        {
            CreateSource(new Rect(0, 0, 50, 44)),
            CreateSource(new Rect(50, 0, 50, 44)),
            CreateSource(new Rect(0, 44, 100, 36)),
        };
        using FilterEffectContext context = Record(Trapezoid(), input);
        Execute(context, targets);
        using Bitmap bitmap = CompositeTiles(targets, new PixelSize(100, 80));
        // The seam projects to y = 30.34. The single bottom tile owns the centers of the pixels in row 30.
        Assert.That(Enumerable.Range(32, 36).Select(x => ImageMetrics.ReadPixel(bitmap, x, 30).Alpha),
            Has.All.GreaterThan(0.98f));
    }

    [Test]
    public void ChainedWarpsUseTheLogicalTileBoundsAtFractionalOffsets()
    {
        var input = new Rect(0, 0, 100, 80);
        CornerPin translate = CreateAbsolute(new Rect(0.25f, 0.25f, 100, 80));
        CornerPin scale = Create(new(0, 0), new(2, 0), new(2, 2), new(0, 2));
        var group = new FilterEffectGroup { Children = { new SplitEffect(), translate, scale } };
        var expected = new Rect(0.25f, 0.25f, 200, 160);
        using FilterEffectContext context = Record(group, input);
        using var targets = new EffectTargets { CreateSource(input) };
        Execute(context, targets);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Bounds, Is.EqualTo(expected));
            Assert.That(targets, Has.Count.EqualTo(4));
            Assert.That(targets.CalculateBounds(), Is.EqualTo(expected));
            foreach (EffectTarget tile in targets)
            {
                Assert.That(tile.Placement.DeviceBounds, Is.EqualTo(PixelRect.FromRect(tile.Bounds, 1)));
            }
        }
    }

    [Test]
    public void TilesShareOnePixelGridWhenOnlyOneTileExceedsTheBudget()
    {
        var input = new Rect(0, 0, 96, 32);
        CornerPin translate = CreateAbsolute(new Rect(0.75f, 0, 96, 32));
        using FilterEffectContext context = Record(translate, input);
        using var targets = new EffectTargets
        {
            CreateSource(new Rect(0, 0, 64, 32)),
            CreateSource(new Rect(64, 0, 32, 32)),
        };
        Execute(context, targets, budget: BufferDimensionBudget.Named(64));

        Assert.That(targets, Has.Count.EqualTo(2));
        float scale = targets[0].Placement.Scale.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(scale, Is.GreaterThan(0.95f).And.LessThan(1f));
            Assert.That(targets[1].Placement.Scale.Value, Is.EqualTo(scale));
            foreach (EffectTarget tile in targets)
            {
                Assert.That(tile.Placement.DeviceBounds, Is.EqualTo(PixelRect.FromRect(tile.Bounds, scale)));
            }
        }
    }

    [TestCase(RenderIntent.Preview)]
    [TestCase(RenderIntent.Delivery)]
    public void DeclinedSecondTileLeavesBothOriginalTargetsInPlace(RenderIntent intent)
    {
        var input = new Rect(0, 0, 100, 80);
        CornerPin translate = CreateAbsolute(new Rect(20, 0, 100, 80));
        using FilterEffectContext context = Record(translate, input);
        EffectTarget first = CreateSource(new Rect(0, 0, 50, 80));
        EffectTarget second = CreateSource(new Rect(50, 0, 50, 80));
        using var targets = new EffectTargets { first, second };
        using var factory = new FailureTestTargetFactory(failAt: 1);
        using var pool = new RenderTargetPool(factory);
        using RenderTargetLeaseSession session = pool.BeginSession(intent);

        if (intent == RenderIntent.Delivery)
            Assert.Throws<RenderTargetAllocationException>(() => Execute(context, targets, intent: intent, session: session));
        else
            Execute(context, targets, intent: intent, session: session);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.CreateCalls, Is.EqualTo(2));
            Assert.That(session.ContentDropObserved, Is.EqualTo(intent == RenderIntent.Preview));
            Assert.That(targets.ToArray(), Is.EqualTo(new[] { first, second }));
        }
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void PartiallySharedEdgesKeepTheirInteriorOpaqueAndExposedPortionsAntialiased(bool horizontalEdge, bool gpu)
    {
        if (gpu)
        {
            TestGpu.RequireAnyDevice();
            RenderThread.Dispatcher.Invoke(() => AssertPartiallySharedEdge(horizontalEdge, gpu: true));
        }
        else
        {
            AssertPartiallySharedEdge(horizontalEdge, gpu: false);
        }
    }

    private static void AssertPartiallySharedEdge(bool horizontalEdge, bool gpu)
    {
        var input = horizontalEdge ? new Rect(0, 0, 80, 100) : new Rect(0, 0, 100, 80);
        var split = new SplitEffect
        {
            HorizontalDivisions = { Value = horizontalEdge ? 1 : 2 },
            VerticalDivisions = { Value = horizontalEdge ? 2 : 1 },
        };
        var shiftSecondTile = new RangeAnimatorEffect
        {
            RangeStart = { Value = 50 },
            Position = { Value = horizontalEdge ? new Point(20, 0) : new Point(0, 20) },
        };
        CornerPin translate = CreateAbsolute(horizontalEdge
            ? new Rect(0, 0.5f, 100, 100)
            : new Rect(0.5f, 0, 100, 100));
        var group = new FilterEffectGroup { Children = { split, shiftSecondTile, translate } };
        using EffectTargets targets = Render(group, input, gpu: gpu);
        using Bitmap bitmap = CompositeTiles(targets, new PixelSize(101, 101), gpu);
        float[] owners = targets.Select(InteriorAlpha).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Enumerable.Range(25, 50).Select(AlongSeam), Has.All.GreaterThan(0.98f));
            Assert.That(AlongSeam(10), Is.InRange(0.4f, 0.6f));
            Assert.That(AlongSeam(90), Is.InRange(0.4f, 0.6f));
            Assert.That(owners, Has.Exactly(1).GreaterThan(0.98f));
            Assert.That(owners, Has.Exactly(1).EqualTo(0f));
        }

        float AlongSeam(int index) => horizontalEdge
            ? ImageMetrics.ReadPixel(bitmap, index, 50).Alpha
            : ImageMetrics.ReadPixel(bitmap, 50, index).Alpha;

        float InteriorAlpha(EffectTarget tile)
        {
            using Bitmap pixels = tile.RenderTarget!.Snapshot();
            return Pixel(pixels, tile, 50, 50).Alpha;
        }
    }

    [Test]
    public void RelativeCornersScaleAndAbsoluteCornersKeepTheirOffsets()
    {
        var effect = new CornerPin { TopLeft = { Value = Relative(0.25f, 0.125f) }, TopRight = { Value = Absolute(60, 8) } };
        var small = new Rect(-20, 10, 80, 40);
        var large = new Rect(5, -5, 160, 80);

        var smallHandles = effect.GetEditingHandles(MediaTime.Zero, small);
        var largeHandles = effect.GetEditingHandles(MediaTime.Zero, large);
        using FilterEffectContext smallRender = Record(effect, small);
        using FilterEffectContext largeRender = Record(effect, large);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(smallHandles[0].Position, Is.EqualTo(new Point(0, 15)));
            Assert.That(largeHandles[0].Position, Is.EqualTo(new Point(45, 5)));
            Assert.That(smallHandles[1].Position, Is.EqualTo(new Point(40, 18)));
            Assert.That(largeHandles[1].Position, Is.EqualTo(new Point(65, 3)));
            Assert.That(smallRender.Bounds, Is.EqualTo(new Rect(-20, 15, 80, 35)));
            Assert.That(largeRender.Bounds, Is.EqualTo(new Rect(5, 3, 160, 72)));
        }
    }

    [Test]
    public void JsonPreservesBaseValuesUnitsAndBothAnimationKinds()
    {
        var effect = new CornerPin
        {
            TopLeft = { Value = Absolute(12, 18) },
            TopRight = { Value = Relative(0.875f, 0.25f) },
            BottomLeft = { Value = Relative(0.125f, 0.875f) },
        };
        effect.TopLeft.SetKeyFrameAnimation(new KeyFrameAnimation<RelativePoint>
        {
            KeyFrames = { Key(MediaTime.Zero, Absolute(13, 14)), Key(MediaTime.FromSeconds(1), Relative(0.25f, 0.125f)) },
        });
        effect.BottomLeft.SetKeyFrameAnimation(new KeyFrameAnimation<RelativePoint>
        {
            KeyFrames = { Key(MediaTime.Zero, Absolute(20, 40)), Key(MediaTime.FromSeconds(1), Relative(0.25f, 0.75f)) },
        });
        ComponentAnimations.Separate(effect.BottomLeft);

        var json = CoreSerializer.SerializeToJsonObject(effect);
        var restored = (CornerPin)CoreSerializer.DeserializeFromJsonObject(json, typeof(CornerPin));
        var whole = restored.TopLeft.GetKeyFrameAnimation()!;
        var separate = (ComponentAnimation<RelativePoint, float>)restored.BottomLeft.GetPropertyAnimation()!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(restored.TopLeft.Value, Is.EqualTo(effect.TopLeft.Value));
            Assert.That(restored.TopRight.Value, Is.EqualTo(effect.TopRight.Value));
            Assert.That(whole.KeyFrames.Select(key => key.Value), Is.EqualTo(new[] { Absolute(13, 14), Relative(0.25f, 0.125f) }));
            Assert.That(separate.Components.Select(component => component.GetKeyFrameAnimation()!.KeyFrames.Count), Is.EqualTo(new[] { 2, 2 }));
            Assert.That(separate.EvaluateAt(MediaTime.Zero), Is.EqualTo(Absolute(20, 40)));
            Assert.That(separate.EvaluateAt(MediaTime.FromSeconds(1)), Is.EqualTo(Relative(0.25f, 0.75f)));
        }
    }

    [Test]
    public void AChangedCornerUpdatesTheResourceVersionAndBounds()
    {
        var effect = new CornerPin();
        effect.TopLeft.SetKeyFrameAnimation(new KeyFrameAnimation<RelativePoint>
        {
            KeyFrames = { Key(MediaTime.Zero, RelativePoint.TopLeft), Key(MediaTime.FromSeconds(2), Relative(-0.25f, 0)) },
        });
        using var resource = effect.ToResource(CompositionContext.Default);
        int startVersion = resource.Version;
        var time = new CompositionContext(MediaTime.FromSeconds(1));
        bool updated = false;
        resource.Reconcile(effect, time, ref updated);
        using var context = new FilterEffectContext(s_input);
        context.ApplyTransactional(resource);
        Assert.That(updated, Is.True);
        Assert.That(resource.Version, Is.EqualTo(startVersion + 1));
        Assert.That(context.Bounds, Is.EqualTo(new Rect(-2.5f, 20, 112.5f, 80)));

        updated = false;
        resource.Reconcile(effect, time, ref updated);
        Assert.That(updated, Is.False);
    }

    [Test]
    public void UnrenderedHandlesUseDrawableSizeAndRemainEditableOnAConcaveQuad()
    {
        var effect = new CornerPin();
        Assert.That(effect.GetEditingHandles(MediaTime.Zero), Is.Empty);
        var shape = new RectShape { Width = { Value = 100 }, Height = { Value = 80 }, FilterEffect = { Value = effect } };
        Assert.That(effect.GetEditingHandles(MediaTime.Zero).Select(handle => handle.Id),
            Is.EqualTo(new[] { "top-left", "top-right", "bottom-right", "bottom-left" }));

        IEditingStageProvider provider = effect;
        provider.BeginEditingHandleDrag("top-left", MediaTime.Zero)(new Point(60, 70));
        Point[] positions = effect.GetEditingHandles(MediaTime.Zero).Select(handle => handle.Position).ToArray();
        AssertPoint(positions[0], new Point(60, 70));
        Assert.That(positions[1..], Is.EqualTo(new Point[] { new(100, 0), new(100, 80), new(0, 80) }));
        Assert.That(provider.GetEditingGuides(MediaTime.Zero), Is.EqualTo(new[]
        {
            new EditingGuideLine(positions[0], positions[1]), new EditingGuideLine(positions[1], positions[2]),
            new EditingGuideLine(positions[2], positions[3]), new EditingGuideLine(positions[3], positions[0]),
        }));

        provider.BeginEditingHandleDrag("top-left", MediaTime.Zero)(new Point(10, 15));
        Assert.That(effect.TopLeft.Value, Is.EqualTo(Relative(0.1f, 0.1875f)));
        GC.KeepAlive(shape);
    }

    [Test]
    public void NonFiniteHandlesAndGuidesAreOmittedIndividually()
    {
        var effect = new CornerPin { TopLeft = { Value = Absolute(float.NaN, 0) } };
        Assert.That(effect.GetEditingHandles(MediaTime.Zero, s_input), Has.Count.EqualTo(3));
        Assert.That(effect.GetEditingGuides(MediaTime.Zero, s_input), Is.EqualTo(new[]
        {
            new EditingGuideLine(s_input.TopRight, s_input.BottomRight), new EditingGuideLine(s_input.BottomRight, s_input.BottomLeft),
        }));
    }

    [Test]
    public void DragPreservesTheCapturedUnitAndDoesNotEditAnExpression()
    {
        var effect = new CornerPin { TopLeft = { Value = Absolute(10, 10) } };
        Action<Point> drag = effect.BeginEditingHandleDrag("top-left", MediaTime.Zero, s_input);
        drag(new Point(44, 35));
        Assert.That(effect.TopLeft.Value, Is.EqualTo(Absolute(34, 15)));

        effect.TopLeft.Expression = new ConstantExpression<RelativePoint>(Relative(0.5f, 0.5f));
        drag(new Point(55, 50));
        effect.BeginEditingHandleDrag("top-left", MediaTime.Zero, s_input)(new Point(55, 50));
        Assert.That(effect.TopLeft.Value, Is.EqualTo(Absolute(34, 15)));
        Assert.That(effect.GetEditingHandles(MediaTime.Zero, s_input)[0].Position, Is.EqualTo(s_input.Center));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AnimatedDragFindsNearFrameKeysAndInsertsAtTheRoundedFrame(bool separate)
    {
        var effect = new CornerPin();
        var original = Key(MediaTime.FromTimeSpan(TimeSpan.FromTicks(1)), Absolute(6, 8));
        effect.TopLeft.SetKeyFrameAnimation(new KeyFrameAnimation<RelativePoint> { KeyFrames = { original } });
        IProperty[] tracks = separate ? ComponentAnimations.Separate(effect.TopLeft).Components.ToArray() : [effect.TopLeft];
        MediaTime frame = MediaTime.FromFrames(7, Project.ReadFrameRate(null));
        var nearFrame = frame + MediaTime.FromTimeSpan(TimeSpan.FromTicks(20));

        effect.BeginEditingHandleDrag("top-left", MediaTime.Zero, s_input)(new Point(22, 35));
        effect.BeginEditingHandleDrag("top-left", nearFrame, s_input)(new Point(30, 50));

        foreach (IProperty track in tracks)
        {
            var keys = track.GetKeyFrameAnimation()!.KeyFrames;
            Assert.That(keys.Select(key => key.KeyTime.Value), Is.EqualTo(new[] { original.KeyTime.Value, frame }));
            Assert.That(keys[1].Easing.Value, Is.TypeOf<SplineEasing>());
        }
        Assert.That(effect.TopLeft.Evaluate(new EvaluationContext(MediaTime.Zero)), Is.EqualTo(Absolute(12, 15)));
        Assert.That(effect.TopLeft.Evaluate(new EvaluationContext(frame)), Is.EqualTo(Absolute(20, 30)));
    }

    [Test]
    public void SeparatedDragUpdatesOnlyComponentsWithoutExpressions()
    {
        var effect = new CornerPin();
        effect.TopLeft.SetKeyFrameAnimation(new KeyFrameAnimation<RelativePoint> { KeyFrames = { Key(MediaTime.Zero, RelativePoint.TopLeft) } });
        var components = ComponentAnimations.Separate(effect.TopLeft);
        var x = (IExpressionProperty<float>)components.Components[0];
        var y = (IExpressionProperty<float>)components.Components[1];
        x.Expression = new ConstantExpression<float>(0.25f);
        MediaTime time = MediaTime.FromSeconds(1);
        effect.BeginEditingHandleDrag("top-left", time, s_input)(new Point(40, 60));
        Assert.That(x.GetKeyFrameAnimation()!.KeyFrames, Has.Count.EqualTo(1));
        Assert.That(y.GetKeyFrameAnimation()!.KeyFrames, Has.Count.EqualTo(2));
        Assert.That(effect.TopLeft.Evaluate(new EvaluationContext(time)), Is.EqualTo(Relative(0.25f, 0.5f)));
    }

    private sealed class ConstantExpression<T>(T value, IExpression<T>? replaced = null) : IReplacingExpression<T>
    {
        public string ExpressionString => string.Empty;
        public IExpression<T>? Replaced => replaced;
        public IExpression<T> WithReplaced(IExpression<T>? expression) => new ConstantExpression<T>(value, expression);
        public T Evaluate(ExpressionContext context) => value;
        public bool Validate(out string? error)
        {
            error = null;
            return true;
        }
    }

    private static RelativePoint Relative(float x, float y) => new(x, y, RelativeUnit.Relative);
    private static RelativePoint Absolute(float x, float y) => new(x, y, RelativeUnit.Absolute);

    private static CornerPin Trapezoid() => Create(new(0.25f, 0), new(0.75f, 0), new(1, 1), new(0, 1));

    private static CornerPin Create(Point tl, Point tr, Point br, Point bl) => new()
    {
        TopLeft = { Value = new RelativePoint(tl, RelativeUnit.Relative) },
        TopRight = { Value = new RelativePoint(tr, RelativeUnit.Relative) },
        BottomRight = { Value = new RelativePoint(br, RelativeUnit.Relative) },
        BottomLeft = { Value = new RelativePoint(bl, RelativeUnit.Relative) },
    };

    private static CornerPin CreateAbsolute(Rect offsets) => new()
    {
        TopLeft = { Value = Absolute(offsets.Left, offsets.Top) },
        TopRight = { Value = Absolute(offsets.Right, offsets.Top) },
        BottomRight = { Value = Absolute(offsets.Right, offsets.Bottom) },
        BottomLeft = { Value = Absolute(offsets.Left, offsets.Bottom) },
    };

    private static KeyFrame<RelativePoint> Key(MediaTime time, RelativePoint value)
        => new() { KeyTime = { Value = time }, Value = { Value = value } };

    private static FilterEffectContext Record(FilterEffect effect, Rect bounds)
    {
        using var resource = effect.ToResource(CompositionContext.Default);
        var context = new FilterEffectContext(bounds);
        context.ApplyTransactional(resource);
        return context;
    }

    private static void Execute(FilterEffectContext context, EffectTargets targets, float workingScale = 1,
        RenderIntent intent = RenderIntent.Preview, BufferDimensionBudget? budget = null, RenderTargetLeaseSession? session = null)
    {
        using var builder = new SKImageFilterBuilder();
        using var executor = new FilterEffectExecutor(targets, builder, new(intent, RenderRequestPurpose.Auxiliary)
        {
            WorkingScale = workingScale,
            MaxWorkingScale = workingScale,
            Budget = budget,
            RenderTargetLeaseSession = session,
        });
        executor.Apply(context);
        executor.Flush(false);
    }

    private static EffectTargets Render(FilterEffect effect, Rect bounds, float sourceScale = 1, float workingScale = 1,
        int apron = 0, bool gpu = false)
    {
        using FilterEffectContext context = Record(effect, bounds);
        var targets = new EffectTargets { CreateSource(bounds, sourceScale, apron, gpu: gpu) };
        try
        {
            Execute(context, targets, workingScale);
            return targets;
        }
        catch
        {
            targets.Dispose();
            throw;
        }
    }

    private static EffectTarget CreateSource(Rect bounds, float scale = 1, int apron = 0,
        Action<SKCanvas, PixelRect>? draw = null, bool gpu = false)
    {
        PixelRect content = PixelRect.FromRect(bounds, scale);
        var footprint = new PixelRect(content.X - apron, content.Y - apron, content.Width + 2 * apron, content.Height + 2 * apron);
        using RenderTarget backing = gpu
            ? RenderTarget.Create(footprint.Width, footprint.Height)!
            : CpuRenderTargets.Create(footprint.Width, footprint.Height);
        SKCanvas canvas = backing.Value.Canvas;
        canvas.Clear(apron > 0 ? SKColors.Magenta : SKColors.Transparent);
        (draw ?? DrawQuadrants)(canvas, new PixelRect(apron, apron, content.Width, content.Height));
        canvas.Flush();
        return new EffectTarget(backing, bounds, EffectiveScale.At(scale), footprint);
    }

    private static void DrawQuadrants(SKCanvas canvas, PixelRect area)
    {
        int w = area.Width / 2;
        int h = area.Height / 2;
        using var paint = new SKPaint { Color = SKColors.Red };
        canvas.DrawRect(area.X, area.Y, w, h, paint);
        paint.Color = SKColors.Lime;
        canvas.DrawRect(area.X + w, area.Y, area.Width - w, h, paint);
        paint.Color = SKColors.Blue;
        canvas.DrawRect(area.X, area.Y + h, w, area.Height - h, paint);
        paint.Color = SKColors.Yellow;
        canvas.DrawRect(area.X + w, area.Y + h, area.Width - w, area.Height - h, paint);
    }

    private static Bitmap CompositeTiles(EffectTargets tiles, PixelSize size, bool gpu = false)
    {
        using RenderTarget backing = gpu
            ? RenderTarget.Create(size.Width, size.Height)!
            : CpuRenderTargets.Create(size.Width, size.Height);
        SKCanvas canvas = backing.Value.Canvas;
        canvas.Clear(SKColors.Transparent);
        foreach (EffectTarget tile in tiles)
        {
            using SKImage image = tile.RenderTarget!.Value.Snapshot();
            Point origin = tile.Placement.LogicalOrigin;
            canvas.DrawImage(image, origin.X, origin.Y, SKSamplingOptions.Default);
        }
        canvas.Flush();
        return backing.Snapshot();
    }

    private static Rgba Pixel(Bitmap bitmap, EffectTarget target, float x, float y)
    {
        RenderPlacement placement = target.Placement;
        Point device = (new Point(x, y) - placement.LogicalOrigin) * placement.Scale.Value;
        return ImageMetrics.ReadPixel(bitmap, (int)MathF.Floor(device.X), (int)MathF.Floor(device.Y));
    }

    private static void AssertPoint(Point actual, Point expected)
    {
        Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.0001f));
        Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.0001f));
    }
}
