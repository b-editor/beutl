using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class PaintedSourceAuthoringContractTests
{
    [Test]
    public void PaintedSource_InvokesItsDrawCallbackAndPaintsWithinItsDeclaredBounds()
    {
        var rect = new Rect(2, 2, 4, 4);
        var record = new PaintRecord(rect);
        using var fill = new SolidColorBrush.Resource { Color = Colors.White, Opacity = 100f };
        using var node = new PluginPaintedRectangleNode(record, fill, null);
        using var renderer = CreateRenderer(node, new Rect(0, 0, 8, 8));
        using var target = new CpuRenderTarget(new PixelSize(8, 8));

        using (var canvas = new ImmediateCanvas(target, RenderIntent.Preview))
        {
            canvas.Clear();
            renderer.Render(canvas);
        }

        using Bitmap bitmap = target.Snapshot();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(record.DrawCalls, Is.EqualTo(1));
            Assert.That(record.ObservedFill, Is.SameAs(fill));
            Assert.That(record.ObservedPen, Is.Null);
            Assert.That(node.DeclaredBounds, Is.EqualTo(rect));

            Assert.That(AlphaAt(bitmap, 2, 2), Is.GreaterThan(0.9f));
            Assert.That(AlphaAt(bitmap, 5, 5), Is.GreaterThan(0.9f));
            Assert.That(AlphaAt(bitmap, 1, 1), Is.LessThan(0.01f));
            Assert.That(AlphaAt(bitmap, 6, 6), Is.LessThan(0.01f));
        }
    }

    [Test]
    public void PaintedSource_ResolvesTheHitTestSlotItBoundForThisRecording()
    {
        var rect = new Rect(2, 2, 4, 4);
        using var fill = new SolidColorBrush.Resource { Color = Colors.White, Opacity = 100f };
        using var node = new PluginPaintedRectangleNode(new PaintRecord(rect), fill, null);
        using var renderer = CreateRenderer(node, new Rect(0, 0, 8, 8));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(renderer.HitTest(new Point(3, 3)), Is.True);
            Assert.That(renderer.HitTest(new Point(7, 7)), Is.False);
        }
    }

    [Test]
    public void PaintedSource_RejectsABindingItDidNotDeclareAndEmptyBounds()
    {
        var straySlot = new RenderResourceSlot<PaintRecord>();
        var rect = new Rect(1, 1, 2, 2);
        Exception? unbound = null;
        Exception? empty = null;

        using var node = new DelegateProbeNode(context =>
        {
            RenderResource<PaintRecord> token = context.Borrow(new PaintRecord(rect));
            unbound = Assert.Catch(() => context.PaintedSource(
                rect,
                static (canvas, fill, pen, current) => canvas.DrawRectangle(current, fill, pen),
                null,
                null,
                rect,
                RenderHitTestContract.OutputBounds,
                RenderScaleContract.Vector,
                bindings: [straySlot.Bind(token)]));
            empty = Assert.Catch(() => context.PaintedSource(
                rect,
                static (canvas, fill, pen, current) => canvas.DrawRectangle(current, fill, pen),
                null,
                null,
                Rect.Empty,
                RenderHitTestContract.OutputBounds,
                RenderScaleContract.Vector));
        });

        Measure(node);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unbound, Is.TypeOf<ArgumentException>());
            Assert.That(empty, Is.TypeOf<ArgumentException>());
        }
    }

    [TestCase(StrokeAlignment.Inside, 4f, 0f)]
    [TestCase(StrokeAlignment.Center, 4f, 0f)]
    [TestCase(StrokeAlignment.Outside, 4f, 0f)]
    [TestCase(StrokeAlignment.Center, 3f, 2f)]
    [TestCase(StrokeAlignment.Outside, 1f, 0f)]
    public void PenHelperBounds_AgreeWithTheEngineShapeNodeStrokeBounds(
        StrokeAlignment alignment,
        float thickness,
        float offset)
    {
        // Far enough inside the render request's target domain that no inflation this case applies can reach
        // its edge, so a measured bound is the node's own declaration rather than a clipped one.
        var rect = new Rect(12, 14, 10, 6);
        using var brush = new SolidColorBrush.Resource { Color = Colors.White, Opacity = 100f };
        using var pen = new Pen.Resource
        {
            Brush = brush,
            Thickness = thickness,
            StrokeAlignment = alignment,
            Offset = offset,
            MiterLimit = 10,
            TrimEnd = 100,
        };

        using var pluginNode = new PluginPaintedRectangleNode(new PaintRecord(rect), brush, pen);
        using var engineNode = new RectangleRenderNode(rect, brush, pen);

        RenderNodeMeasurement plugin = Measure(pluginNode);
        RenderNodeMeasurement engine = Measure(engineNode);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.OutputBounds, Is.EqualTo(PenHelper.GetBounds(rect, pen)));
            Assert.That(plugin.OutputBounds, Is.EqualTo(engine.OutputBounds));
            Assert.That(pluginNode.DeclaredBounds, Is.EqualTo(engine.OutputBounds));
        }
    }

    [Test]
    public void TryCalculateRecordedOutputExtent_UnionsTheFootprintOfTheFragmentsANodeRecorded()
    {
        var first = new Rect(1, 2, 4, 4);
        var second = new Rect(8, 3, 2, 6);
        using var fill = new SolidColorBrush.Resource { Color = Colors.White, Opacity = 100f };
        using var node = new RecordedExtentProbeNode(fill, first, second, null);

        Measure(node);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(node.ExtentIsFinite, Is.True);
            Assert.That(node.Extent, Is.EqualTo(first.Union(second)));
        }
    }

    [Test]
    public void TryCalculateRecordedOutputExtent_CoversAFiniteTargetWriteRecordedAlongside()
    {
        var first = new Rect(1, 2, 4, 4);
        var second = new Rect(8, 3, 2, 6);
        var written = new Rect(0, 0, 1, 1);
        using var fill = new SolidColorBrush.Resource { Color = Colors.White, Opacity = 100f };
        using var node = new RecordedExtentProbeNode(fill, first, second, TargetRegion.Region(written));

        Measure(node);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(node.ExtentIsFinite, Is.True);
            Assert.That(node.Extent, Is.EqualTo(first.Union(second).Union(written)));
        }
    }

    [Test]
    public void TryCalculateRecordedOutputExtent_ReportsFailureForASymbolicTargetWrite()
    {
        var first = new Rect(1, 2, 4, 4);
        var second = new Rect(8, 3, 2, 6);
        using var fill = new SolidColorBrush.Resource { Color = Colors.White, Opacity = 100f };
        using var node = new RecordedExtentProbeNode(fill, first, second, TargetRegion.Full);

        Measure(node);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(node.ExtentIsFinite, Is.False);
            Assert.That(node.Extent, Is.EqualTo(default(Rect)));
        }
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode node, Rect targetDomain)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    OutputScale = 1,
                    TargetDomain = targetDomain,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = new CpuTargetFactory(),
            });

    private static RenderNodeMeasurement Measure(RenderNode node)
    {
        using RenderNodeRenderer renderer = CreateRenderer(node, new Rect(0, 0, 64, 64));
        return renderer.Measure();
    }

    private static float AlphaAt(Bitmap bitmap, int x, int y)
    {
        Assert.That(bitmap.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
        return (float)bitmap.GetRow<Half>(y)[x * 4 + 3];
    }

    private sealed class PaintRecord(Rect rect)
    {
        public Rect Rect { get; } = rect;

        public int DrawCalls { get; private set; }

        public Brush.Resource? ObservedFill { get; private set; }

        public Pen.Resource? ObservedPen { get; private set; }

        public void Record(Brush.Resource? fill, Pen.Resource? pen)
        {
            DrawCalls++;
            ObservedFill = fill;
            ObservedPen = pen;
        }
    }

    private sealed class PluginPaintedRectangleNode(PaintRecord record, Brush.Resource? fill, Pen.Resource? pen)
        : RenderNode
    {
        private static readonly RenderResourceSlot<PaintRecord> s_recordSlot = new();

        private static readonly RenderHitTestContract s_hitTest = RenderHitTestContract.FromSlot(
            s_recordSlot,
            static (state, point) => state.Rect.ContainsExclusive(point));

        private static readonly RenderResourceSlot[] s_slots = [s_recordSlot];

        public Rect DeclaredBounds { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            Rect bounds = PenHelper.GetBounds(record.Rect, pen);
            DeclaredBounds = bounds;
            context.Publish(context.PaintedSource(
                record,
                static (canvas, currentFill, currentPen, state) =>
                {
                    state.Record(currentFill, currentPen);
                    canvas.DrawRectangle(state.Rect, currentFill, currentPen);
                },
                fill,
                pen,
                bounds,
                s_hitTest,
                RenderScaleContract.Vector,
                bindings: [s_recordSlot.Bind(context.Borrow(record))],
                slots: s_slots));
        }
    }

    private sealed class DelegateProbeNode(Action<RenderNodeContext> process) : RenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }

    private sealed class RecordedExtentProbeNode(
        Brush.Resource fill,
        Rect first,
        Rect second,
        TargetRegion? targetWrite)
        : RenderNode
    {
        public bool ExtentIsFinite { get; private set; }

        public Rect Extent { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            var recorded = new List<RenderFragmentHandle>
            {
                Paint(context, first),
                Paint(context, second),
            };

            if (targetWrite is { } region)
            {
                recorded.Add(context.TargetCommand(
                    [],
                    RenderDescriptionFactory.TargetCommand(
                        static _ => { },
                        region,
                        Rect.Empty,
                        RenderHitTestContract.None)));
            }

            ExtentIsFinite = context.TryCalculateRecordedOutputExtent(recorded, out Rect extent);
            Extent = extent;
            context.PublishRange(recorded);
        }

        private RenderFragmentHandle Paint(RenderNodeContext context, Rect rect)
            => context.PaintedSource(
                rect,
                static (canvas, currentFill, currentPen, current) =>
                    canvas.DrawRectangle(current, currentFill, currentPen),
                fill,
                null,
                rect,
                RenderHitTestContract.OutputBounds,
                RenderScaleContract.Vector);
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => new CpuRenderTarget(allocation.DeviceSize);
    }

    private sealed class CpuRenderTarget : RenderTarget
    {
        private static readonly SKColorSpace s_colorSpace = SKColorSpace.CreateSrgbLinear();

        public CpuRenderTarget(PixelSize size)
            : base(CreateSurface(size), size.Width, size.Height)
        {
        }

        private static SKSurface CreateSurface(PixelSize size)
        {
            return SKSurface.Create(new SKImageInfo(
                       size.Width,
                       size.Height,
                       SKColorType.RgbaF16,
                       SKAlphaType.Premul,
                       s_colorSpace))
                   ?? throw new InvalidOperationException("Could not create a CPU contract-test surface.");
        }
    }
}
