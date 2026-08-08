using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.PublicApiContractTests;

/// <summary>
/// Proves that an out-of-tree render node can lower a <see cref="DrawableBrush"/> and paint with it using
/// only public API, and that the lowered content's identity reaches the output-cache key.
/// </summary>
[TestFixture]
public sealed class LoweredBrushAuthoringContractTests
{
    private static readonly Rect s_rect = new(0, 0, 64, 36);

    [Test]
    public void RawDrawableBrushResource_IsRejectedByThePublicDrawOverload()
    {
        using var content = new DrawableContent(Colors.White);
        using var node = new UnloweredDrawableBrushNode(content.BrushResource, s_rect);
        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Disabled);

        Exception? failure = Assert.Catch(() => renderer.Rasterize().Dispose());

        TestContext.Out.WriteLine(failure?.ToString());
        Assert.That(
            failure,
            Is.TypeOf<InvalidOperationException>().With.Message.Contains("must be lowered"),
            "a raw DrawableBrush.Resource has no lowered content, so the paint cannot be built");
    }

    [Test]
    public void LoweredDrawableBrush_PaintsNestedContentThroughThePublicRecordingPath()
    {
        using var content = new DrawableContent(Colors.White);
        using var node = new LoweredPaintEllipseNode(s_rect, content.BrushResource, pen: null);
        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Disabled);

        RenderNodeMeasurement measurement = renderer.Measure();
        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.OutputBounds, Is.EqualTo(s_rect));
            Assert.That(rasterization.Bitmap, Is.Not.Null);
            Assert.That(ReadPixel(rasterization, 32, 18), Is.EqualTo(Linear(1, 1, 1, 1)).Within(0.01f));
            Assert.That(ReadPixel(rasterization, 1, 1).A, Is.LessThan(0.01f),
                "the ellipse geometry still clips the drawable brush");
            Assert.That(node.ValueEligible, Is.True,
                "a painted source is an ordinary value, not a target scope");
            Assert.That(node.ContributesValues, Is.True);
            Assert.That(measurement.ValueCardinality, Is.EqualTo(RenderValueCardinality.Single));
        });
    }

    [Test]
    public void CapturingDrawCallback_IsRejectedAtRecordingTime()
    {
        using var node = new CapturingDrawNode(s_rect);
        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Disabled);

        ArgumentException? rejection = Assert.Throws<ArgumentException>(() => renderer.Measure());

        TestContext.Out.WriteLine(rejection!.Message);
        Assert.Multiple(() =>
        {
            Assert.That(rejection.ParamName, Is.EqualTo("draw"));
            Assert.That(rejection.Message, Does.Contain("state"),
                "the message must name the channel the captured value has to move into");
        });
    }

    [Test]
    public void MixedPaint_CombinesADeclarativeFillWithADrawableBrushPen()
    {
        using var content = new DrawableContent(Colors.Lime);
        var pen = new Pen
        {
            Thickness = { CurrentValue = 8 },
            StrokeAlignment = { CurrentValue = StrokeAlignment.Inside },
            Brush = { CurrentValue = content.Brush },
        };
        using Pen.Resource penResource = pen.ToResource(CompositionContext.Default);
        var fill = new SolidColorBrush { Color = { CurrentValue = Colors.Red } };
        using Brush.Resource fillResource = (Brush.Resource)fill.ToResource(CompositionContext.Default);
        using var node = new LoweredPaintEllipseNode(s_rect, fillResource, penResource);
        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Disabled);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(ReadPixel(rasterization, 32, 18), Is.EqualTo(Linear(1, 0, 0, 1)).Within(0.01f),
                "the declarative fill still paints the interior");
            Assert.That(ReadPixel(rasterization, 32, 2), Is.EqualTo(Linear(0, 1, 0, 1)).Within(0.05f),
                "the lowered drawable brush paints the stroke");
        });
    }

    [Test]
    public void IdenticalSecondFrame_ReusesTheOutputCache()
    {
        using var content = new DrawableContent(Colors.White);
        using var node = new LoweredPaintEllipseNode(s_rect, content.BrushResource, pen: null);
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Enabled);

        using (RenderNodeRasterization first = renderer.Rasterize())
        {
            Assert.That(ReadPixel(first, 32, 18), Is.EqualTo(Linear(1, 1, 1, 1)).Within(0.01f));
        }

        int afterFirst = node.ExecuteCount;
        using RenderNodeRasterization second = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(afterFirst, Is.EqualTo(1));
            Assert.That(node.ExecuteCount, Is.EqualTo(1),
                "an identical second frame is served from the cached output");
            Assert.That(ReadPixel(second, 32, 18), Is.EqualTo(Linear(1, 1, 1, 1)).Within(0.01f));
        });
    }

    [Test]
    public void ChangedNestedContent_MissesTheOutputCache()
    {
        using var content = new DrawableContent(Colors.White);
        using var node = new LoweredPaintEllipseNode(s_rect, content.BrushResource, pen: null);
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Enabled);

        renderer.Rasterize().Dispose();
        renderer.Rasterize().Dispose();
        int beforeChange = node.ExecuteCount;

        content.SetColor(Colors.Blue);
        using RenderNodeRasterization third = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(beforeChange, Is.EqualTo(1), "the identical frame was a cache hit");
            Assert.That(node.ExecuteCount, Is.EqualTo(2),
                "the nested drawable's version must reach the output-cache key");
            Assert.That(ReadPixel(third, 32, 18), Is.EqualTo(Linear(0, 0, 1, 1)).Within(0.01f),
                "a stale frame here is exactly the defect the cache key must prevent");
        });
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode node, RenderCacheOptions cacheOptions)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_rect,
                    CacheOptions = cacheOptions,
                    Purpose = RenderRequestPurpose.Frame,
                },
                TargetFactory = new CpuTargetFactory(),
            });

    private static (float R, float G, float B, float A) ReadPixel(
        RenderNodeRasterization rasterization,
        int x,
        int y)
    {
        Bitmap bitmap = rasterization.Bitmap
                        ?? throw new AssertionException("The rasterization produced no bitmap.");
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        int index = ((y * bitmap.Width) + x) * 4;
        return (
            ToFloat(pixels[index]),
            ToFloat(pixels[index + 1]),
            ToFloat(pixels[index + 2]),
            ToFloat(pixels[index + 3]));
    }

    private static float ToFloat(ushort half) => (float)BitConverter.UInt16BitsToHalf(half);

    private static (float R, float G, float B, float A) Linear(float r, float g, float b, float a) => (r, g, b, a);

    /// <summary>A drawable brush whose nested content can be repainted between frames.</summary>
    private sealed class DrawableContent : IDisposable
    {
        private readonly SolidColorBrush _contentFill;
        private readonly RectShape _content;

        public DrawableContent(Color color)
        {
            _contentFill = new SolidColorBrush { Color = { CurrentValue = color } };
            _content = new RectShape
            {
                Width = { CurrentValue = (float)s_rect.Width },
                Height = { CurrentValue = (float)s_rect.Height },
                Fill = { CurrentValue = _contentFill },
            };
            Brush = new DrawableBrush(_content) { Stretch = { CurrentValue = Stretch.Fill } };
            BrushResource = Brush.ToResource(CompositionContext.Default);
        }

        public DrawableBrush Brush { get; }

        public DrawableBrush.Resource BrushResource { get; }

        public void SetColor(Color color)
        {
            _contentFill.Color.CurrentValue = color;
            bool updateOnly = false;
            BrushResource.Update(Brush, CompositionContext.Default, ref updateOnly);
        }

        public void Dispose() => BrushResource.Dispose();
    }

    /// <summary>The out-of-tree node under test: it lowers its paint and draws one ellipse with it.</summary>
    private sealed class LoweredPaintEllipseNode(Rect rect, Brush.Resource? fill, Pen.Resource? pen)
        : RenderNode
    {
        private readonly ExecutionProbe _probe = new();

        public int ExecuteCount => _probe.Count;

        public bool ValueEligible { get; private set; }

        public bool ContributesValues { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            Rect outputBounds = rect;
            RenderFragmentHandle painted = context.PaintedSource(
                state: (rect, _probe),
                draw: static (session, state) =>
                {
                    state.Item2.Record();
                    session.Canvas.DrawEllipse(state.Item1, session.Fill, session.Pen);
                },
                fill: fill.Capture(),
                pen: pen.Capture(),
                brushBounds: outputBounds,
                outputBounds: outputBounds,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector,
                structuralKey: "lowered-paint-ellipse");
            ValueEligible = painted.CanBeUsedAsValueInput;
            ContributesValues = painted.ContributesValuesToTarget;
            context.Publish(painted);
        }
    }

    private sealed class CapturingDrawNode(Rect rect) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.PaintedSource(
                state: rect,
                draw: (session, _) => session.Canvas.DrawEllipse(rect, session.Fill, session.Pen),
                fill: null,
                pen: null,
                brushBounds: rect,
                outputBounds: rect,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector,
                structuralKey: "capturing-draw"));
        }
    }

    private sealed class ExecutionProbe
    {
        public int Count { get; private set; }

        public void Record() => Count++;
    }

    private sealed class UnloweredDrawableBrushNode(Brush.Resource fill, Rect rect) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderResource<Brush.Resource> fillToken = context.Borrow(
                fill,
                fill.GetOriginal().Id,
                fill.Version);
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                rect,
                static (session, state) =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(session.RequiredRegion);
                    output.Canvas.Use(canvas => session.UseDeclaredResource<Brush.Resource>(
                        0,
                        brush => canvas.DrawEllipse(state, brush, null)));
                    session.Publish(output);
                },
                bounds: OpaqueRenderBoundsContract.Source(rect),
                hitTest: RenderHitTestContract.OutputBounds,
                valueCardinality: RenderValueCardinality.Single,
                scale: RenderScaleContract.Vector,
                structuralKey: "unlowered-drawable-brush",
                resources: [fillToken]);
            context.Publish(context.OpaqueSource(description));
        }
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => new CpuRenderTarget(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            width,
            height);
}
