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
        long positional = MeasureBytesPerRasterization(usePrimary: false);
        long primary = MeasureBytesPerRasterization(usePrimary: true);

        TestContext.Out.WriteLine($"positional form: {positional} bytes/rasterization");
        TestContext.Out.WriteLine($"primary form: {primary} bytes/rasterization");
        Assert.That(primary, Is.LessThanOrEqualTo(positional),
            "the recorder took over the author's index, type argument and nested closure, so it must not have "
            + "added an allocation to a path that runs per node per frame");
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

    private static long MeasureBytesPerRasterization(bool usePrimary)
    {
        const int Iterations = 200;
        const int Rounds = 7;
        RasterizeRepeatedly(usePrimary, 50);

        long quietestRound = long.MaxValue;
        for (int round = 0; round < Rounds; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            RasterizeRepeatedly(usePrimary, Iterations);
            long after = GC.GetAllocatedBytesForCurrentThread();
            quietestRound = Math.Min(quietestRound, after - before);
        }

        return quietestRound / Iterations;
    }

    private static void RasterizeRepeatedly(bool usePrimary, int iterations)
    {
        using var shared = new SharedPaint();
        using var node = new PaintedNode(shared, usePrimary);
        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Disabled);
        for (int index = 0; index < iterations; index++)
        {
            using RenderNodeRasterization rasterization = renderer.Rasterize();
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

        public SharedPaint()
        {
            Geometry = _geometry.ToResource(CompositionContext.Default);
            Fill = (SolidColorBrush.Resource)new SolidColorBrush(Colors.Red)
                .ToResource(CompositionContext.Default);
        }

        public Geometry.Resource Geometry { get; }

        public SolidColorBrush.Resource Fill { get; }

        public DrawCounter Counter { get; } = new();

        public void Dispose()
        {
            Geometry.Dispose();
            Fill.Dispose();
        }
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
