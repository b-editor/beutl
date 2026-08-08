using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

/// <summary>
/// Pins what the primary-resource form of <see cref="RenderNodeContext.PaintedSource"/> declares and what it
/// costs, against the positional form the in-tree sources used before it.
/// </summary>
[TestFixture]
public sealed class PrimaryPaintedSourceTests
{
    private static readonly Rect s_bounds = new(0, 0, 8, 8);

    [Test]
    public void ThePrimaryFormDeclaresTheSameResourcesInTheSameOrderAsThePositionalForm()
    {
        using var shared = new SharedPaint();
        using var positional = new PaintedNode(shared, usePrimary: false);
        using var primary = new PaintedNode(shared, usePrimary: true);

        Assert.That(DeclaredKeys(primary), Is.EqualTo(DeclaredKeys(positional)),
            "the recorder prepending the primary must reproduce exactly what the author wrote as element 0, "
            + "because RenderCacheResolver.AddResources writes the count and every identity in order into the "
            + "output-cache key");
    }

    /// <remarks>
    /// The wrapper the recorder builds around the three-argument callback is not the runtime identity — the
    /// author's state still is — so an unchanged second frame must still reach the first frame's output.
    /// </remarks>
    [Test]
    public void ThePrimaryFormKeepsTheOutputCacheHitThePositionalFormEarned()
    {
        using var positionalPaint = new SharedPaint();
        using var primaryPaint = new SharedPaint();
        using var positional = new PaintedNode(positionalPaint, usePrimary: false);
        using var primary = new PaintedNode(primaryPaint, usePrimary: true);

        RasterizeTwice(positional);
        RasterizeTwice(primary);

        Assert.Multiple(() =>
        {
            Assert.That(positional.DrawCount, Is.EqualTo(1));
            Assert.That(primary.DrawCount, Is.EqualTo(1),
                "the second frame must be served from the cached output without re-invoking the draw callback");
        });
    }

    /// <remarks>
    /// The measurement covers a whole rasterization, so the shared scaffold — targets, the request family, the
    /// output bitmap — dwarfs the per-form difference. Taking the quietest of several rounds keeps the estimate
    /// from depending on which round an unrelated allocation landed in; allocation noise only ever adds.
    /// </remarks>
    [Test]
    public void ThePrimaryFormAllocatesNoMorePerRasterizationThanThePositionalForm()
    {
        long positional = MeasureBytesPerRasterization(
            static shared => new PaintedNode(shared, usePrimary: false));
        long primary = MeasureBytesPerRasterization(
            static shared => new PaintedNode(shared, usePrimary: true));

        TestContext.Out.WriteLine($"positional form: {positional} bytes/rasterization");
        TestContext.Out.WriteLine($"primary form: {primary} bytes/rasterization");
        TestContext.Out.WriteLine($"difference: {primary - positional} bytes/rasterization");
        Assert.That(primary, Is.LessThanOrEqualTo(positional),
            "the recorder took over the author's index, type argument and nested closure, so it must not have "
            + "added an allocation to a path that runs per node per frame");
    }

    /// <summary>
    /// The same comparison on the shipped <see cref="GeometryRenderNode"/> rather than on a node built for the
    /// test, against a reconstruction whose <c>Process</c> body differs only in recording the drawing through
    /// the positional overload.
    /// </summary>
    /// <remarks>
    /// Both nodes run in this one process against the same engine resources and the same paint, so the figure
    /// is a difference between two recordings of one drawing rather than between two builds, and it isolates
    /// the overload from the rest of the branch. The absolute totals move with the shared rasterization
    /// scaffold; only the difference is a property of the two overloads.
    /// </remarks>
    [Test]
    public void TheShippedGeometryRenderNodeAllocatesNoMoreThanThePositionalFormOfItsOwnBody()
    {
        long positional = MeasureBytesPerRasterization(
            static shared => new PositionalGeometryRenderNode(shared));
        long shipped = MeasureBytesPerRasterization(
            static shared => new GeometryRenderNode(shared.Geometry, shared.Fill, shared.Pen));
        long positionalRecording = MeasureBytesPerRecording(
            static shared => new PositionalGeometryRenderNode(shared));
        long shippedRecording = MeasureBytesPerRecording(
            static shared => new GeometryRenderNode(shared.Geometry, shared.Fill, shared.Pen));

        TestContext.Out.WriteLine($"positional body: {positional} bytes/rasterization, "
            + $"{positionalRecording} bytes/recording");
        TestContext.Out.WriteLine($"shipped node: {shipped} bytes/rasterization, "
            + $"{shippedRecording} bytes/recording");
        TestContext.Out.WriteLine($"difference: {shipped - positional} bytes/rasterization, "
            + $"{shippedRecording - positionalRecording} bytes/recording");
        Assert.Multiple(() =>
        {
            Assert.That(shipped, Is.LessThanOrEqualTo(positional));
            Assert.That(shippedRecording, Is.LessThanOrEqualTo(positionalRecording));
        });
    }

    private static object?[] DeclaredKeys(RenderNode node)
    {
        using var owner = new RenderRequestOwner();
        using var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            maxWorkingScale: 1,
            owner: owner));
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        RenderFragmentId rootId = graph.PublicationRoots.Single();
        var root = (RenderFragmentReference)graph.Fragments
            .Single(fragment => fragment.Id == rootId)
            .Payload!;
        var payload = (OpaqueRenderFragmentPayload)root.Payload!;
        return [.. payload.Description.Resources.Select(static resource => resource.CacheIdentity.Key)];
    }

    private static void RasterizeTwice(RenderNode node)
    {
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Enabled);
        using (RenderNodeRasterization first = renderer.Rasterize())
        {
            Assert.That(first.IsEmpty, Is.False);
        }

        using RenderNodeRasterization second = renderer.Rasterize();
        Assert.That(second.IsEmpty, Is.False);
    }

    private static long MeasureBytesPerRasterization(Func<SharedPaint, RenderNode> create)
        => MeasureQuietestRound(iterations => RasterizeRepeatedly(create, iterations));

    private static long MeasureBytesPerRecording(Func<SharedPaint, RenderNode> create)
        => MeasureQuietestRound(iterations => RecordRepeatedly(create, iterations));

    private static long MeasureQuietestRound(Action<int> run)
    {
        const int Iterations = 200;
        const int Rounds = 7;
        run(50);

        long quietestRound = long.MaxValue;
        for (int round = 0; round < Rounds; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            run(Iterations);
            long after = GC.GetAllocatedBytesForCurrentThread();
            quietestRound = Math.Min(quietestRound, after - before);
        }

        return quietestRound / Iterations;
    }

    private static void RasterizeRepeatedly(Func<SharedPaint, RenderNode> create, int iterations)
    {
        using var shared = new SharedPaint();
        using RenderNode node = create(shared);
        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Disabled);
        for (int index = 0; index < iterations; index++)
        {
            using RenderNodeRasterization rasterization = renderer.Rasterize();
        }
    }

    private static void RecordRepeatedly(Func<SharedPaint, RenderNode> create, int iterations)
    {
        using var shared = new SharedPaint();
        using RenderNode node = create(shared);
        using var owner = new RenderRequestOwner();
        for (int index = 0; index < iterations; index++)
        {
            using var request = new RenderRequest(new RenderRequestOptions(
                RenderIntent.Preview,
                RenderRequestPurpose.Auxiliary,
                outputScale: 1,
                maxWorkingScale: 1,
                owner: owner));
            _ = new RenderRequestRecorder(request).Record(node);
        }
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode node, RenderCacheOptions cacheOptions)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_bounds,
                    CacheOptions = cacheOptions,
                    Purpose = RenderRequestPurpose.Frame,
                },
            });

    /// <remarks>
    /// The engine resources are shared so the two variants declare the same identities, which is the whole
    /// point of the order comparison: the only difference between them is which overload records the source.
    /// </remarks>
    private sealed class SharedPaint : IDisposable
    {
        private readonly EllipseGeometry _geometry = new()
        {
            Width = { CurrentValue = 8 },
            Height = { CurrentValue = 8 },
        };

        private readonly Pen _pen = new()
        {
            Brush = { CurrentValue = Brushes.Black },
            Thickness = { CurrentValue = 2 },
        };

        public SharedPaint()
        {
            Geometry = _geometry.ToResource(CompositionContext.Default);
            Fill = (SolidColorBrush.Resource)new SolidColorBrush(Colors.Red)
                .ToResource(CompositionContext.Default);
            Pen = _pen.ToResource(CompositionContext.Default);
        }

        public Geometry.Resource Geometry { get; }

        public SolidColorBrush.Resource Fill { get; }

        public Pen.Resource Pen { get; }

        public DrawCounter Counter { get; } = new();

        public void Dispose()
        {
            Geometry.Dispose();
            Fill.Dispose();
            Pen.Dispose();
        }
    }

    /// <remarks>
    /// A copy of <see cref="GeometryRenderNode"/>'s <c>Process</c> body with one edit: the drawing is recorded
    /// through the positional overload, so the geometry travels in <c>resources</c> and is reached by index.
    /// Its hit-test state is a local equivalent of the node's private one.
    /// </remarks>
    private sealed class PositionalGeometryRenderNode(SharedPaint shared) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            (Geometry.Resource Resource, int Version) geometrySnapshot = shared.Geometry.Capture()!.Value;
            (Brush.Resource Resource, int Version)? fillSnapshot = shared.Fill.Capture();
            (Pen.Resource Resource, int Version)? penSnapshot = shared.Pen.Capture();
            Geometry.Resource geometry = geometrySnapshot.Resource;
            Brush.Resource? fill = fillSnapshot?.Resource;
            Pen.Resource? pen = penSnapshot?.Resource;
            Rect bounds = PenHelper.CalculateBoundsWithStrokeCap(geometry.GetRenderBounds(pen), pen);
            if (bounds.Width == 0 || bounds.Height == 0)
                return;

            RenderResource<Geometry.Resource> geometryResource = context.Borrow(geometrySnapshot);
            var hitTestState = new HitTestState(geometry, fill, pen);
            var hitTestIdentity = new HitTestIdentity(
                geometry.GetOriginal().Id,
                geometrySnapshot.Version,
                fill?.GetOriginal().Id,
                fillSnapshot?.Version,
                pen?.GetOriginal().Id,
                penSnapshot?.Version);
            RenderResource<HitTestState> hitTestResource = context.Borrow(hitTestState, hitTestIdentity);

            context.Publish(context.PaintedSource(
                state: bounds,
                draw: static (session, _) => session.UseDeclaredResource<Geometry.Resource>(
                    0,
                    geometry => session.Canvas.DrawGeometry(geometry, session.Fill, session.Pen)),
                fill: fillSnapshot,
                pen: penSnapshot,
                brushBounds: bounds,
                outputBounds: bounds,
                hitTest: RenderHitTestContract.FromResource(
                    hitTestResource,
                    static (state, point) => state.HitTest(point),
                    typeof(HitTestState)),
                scale: RenderScaleContract.Vector,
                structuralKey: typeof(GeometryRenderNode),
                resources: [geometryResource, hitTestResource]));
        }

        private sealed class HitTestState(
            Geometry.Resource geometry,
            Brush.Resource? fill,
            Pen.Resource? pen)
        {
            public bool HitTest(Point point)
            {
                return (fill is not null && geometry.FillContains(point))
                       || (pen is not null && geometry.StrokeContains(pen, point));
            }
        }

        private readonly record struct HitTestIdentity(
            Guid GeometryId,
            int GeometryVersion,
            Guid? FillId,
            int? FillVersion,
            Guid? PenId,
            int? PenVersion);
    }

    private sealed class PaintedNode(SharedPaint shared, bool usePrimary) : RenderNode
    {
        public int DrawCount => shared.Counter.Count;

        public override void Process(RenderNodeContext context)
        {
            RenderResource<Geometry.Resource> geometry = context.Borrow(shared.Geometry.Capture()!.Value);
            RenderResource<DrawCounter> counter =
                context.Borrow(shared.Counter, cacheKey: "counter", version: 1);
            context.Publish(usePrimary
                ? context.PaintedSource(
                    primary: geometry,
                    state: s_bounds,
                    draw: static (session, current, _) =>
                    {
                        session.UseDeclaredResource<DrawCounter>(0, static value => value.Record());
                        session.Canvas.DrawGeometry(current, session.Fill, session.Pen);
                    },
                    fill: (shared.Fill, shared.Fill.Version),
                    pen: null,
                    brushBounds: s_bounds,
                    outputBounds: s_bounds,
                    hitTest: RenderHitTestContract.OutputBounds,
                    scale: RenderScaleContract.Vector,
                    structuralKey: typeof(PaintedNode),
                    resources: [counter])
                : context.PaintedSource(
                    state: s_bounds,
                    draw: static (session, _) => session.UseDeclaredResource<Geometry.Resource>(
                        0,
                        current =>
                        {
                            session.UseDeclaredResource<DrawCounter>(1, static value => value.Record());
                            session.Canvas.DrawGeometry(current, session.Fill, session.Pen);
                        }),
                    fill: (shared.Fill, shared.Fill.Version),
                    pen: null,
                    brushBounds: s_bounds,
                    outputBounds: s_bounds,
                    hitTest: RenderHitTestContract.OutputBounds,
                    scale: RenderScaleContract.Vector,
                    structuralKey: typeof(PaintedNode),
                    resources: [geometry, counter]));
        }
    }

    private sealed class DrawCounter
    {
        public int Count { get; private set; }

        public void Record() => Count++;
    }
}
