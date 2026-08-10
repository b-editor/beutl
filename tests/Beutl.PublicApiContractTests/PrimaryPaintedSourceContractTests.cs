using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

/// <summary>
/// Covers the primary-resource form of <see cref="RenderNodeContext.PaintedSource"/>: the recorder declares one
/// resource and hands it to the draw callback already leased.
/// </summary>
/// <remarks>
/// It replaces the index, the type argument, the <c>UseDeclaredResource</c> call and the nested closure a
/// painted source needed to reach the one resource it draws. The positional form stays for a callback that
/// needs a second resource, and its indices still address the author's own <c>resources</c> from zero: the
/// primary is the recorder's declaration, like the lowered paint's slots, so it shifts nothing.
/// </remarks>
[TestFixture]
public sealed class PrimaryPaintedSourceContractTests
{
    private static readonly Rect s_bounds = new(0, 0, 8, 8);

    [Test]
    public void ThePrimaryResource_ReachesTheDrawCallbackAsAParameter()
    {
        List<string> reached = [];
        Payload? borrowed = null;
        using var node = new DelegateNode(context =>
        {
            var payload = new Payload("primary", reached);
            borrowed = payload;
            context.Publish(context.PaintedSource(
                primary: context.Borrow(payload, cacheKey: "primary", version: 1),
                state: s_bounds,
                draw: static (session, primary, _) =>
                {
                    primary.Touch();
                    session.Canvas.DrawRectangle(s_bounds, session.Fill, session.Pen);
                },
                fill: (Brushes.Resource.Red, Brushes.Resource.Red.Version),
                pen: null,
                brushBounds: s_bounds,
                outputBounds: s_bounds,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector,
                structuralKey: typeof(PrimaryPaintedSourceContractTests)));
        });

        using RenderNodeRasterization rasterization = Rasterize(node, RenderCacheOptions.Disabled);

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(borrowed!.TouchCount, Is.EqualTo(1),
                "the recorder leases the primary resource for the callback, so the borrowed value itself "
                + "arrives rather than a token the callback would have to address");
            Assert.That(reached, Is.EqualTo(new[] { "primary" }));
        });
    }

    /// <summary>
    /// A callback needing two or more resources uses stable named bindings, and declaring a primary must not
    /// change any of those names.
    /// </summary>
    [Test]
    public void TheAuthorsResources_KeepTheirStableNamesWhenAPrimaryIsDeclared()
    {
        List<string> reached = [];
        using var node = new DelegateNode(context =>
        {
            RenderResource<Payload> primary =
                context.Borrow(new Payload("primary", reached), cacheKey: "a", version: 1);
            RenderResource<Payload> second =
                context.Borrow(new Payload("second", reached), cacheKey: "b", version: 1);
            RenderResource<Payload> third =
                context.Borrow(new Payload("third", reached), cacheKey: "c", version: 1);
            context.Publish(context.PaintedSource(
                primary: primary,
                state: s_bounds,
                draw: static (session, current, _) =>
                {
                    current.Touch();
                    session.UseDeclaredResource<Payload>("second", static value => value.Touch());
                    session.UseDeclaredResource<Payload>("third", static value => value.Touch());
                    session.Canvas.DrawRectangle(s_bounds, session.Fill, session.Pen);
                },
                fill: (Brushes.Resource.Red, Brushes.Resource.Red.Version),
                pen: null,
                brushBounds: s_bounds,
                outputBounds: s_bounds,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector,
                structuralKey: typeof(PrimaryPaintedSourceContractTests),
                resources: [second.Bind("second"), third.Bind("third")]));
        });

        using RenderNodeRasterization rasterization = Rasterize(node, RenderCacheOptions.Disabled);

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(reached, Is.EqualTo(new[] { "primary", "second", "third" }),
                "the callback addresses the author's resources by stable name; the primary reaches it as a "
                + "separate typed parameter");
        });
    }

    /// <summary>
    /// The primary is leased for the whole callback and remains outside the callback's named binding space.
    /// </summary>
    [Test]
    public void ThePrimary_IsNotReachableThroughTheCallbacksNamedSpace()
    {
        using var node = new DelegateNode(context => context.Publish(context.PaintedSource(
            primary: context.Borrow(new Payload("primary", []), cacheKey: "a", version: 1),
            state: s_bounds,
            draw: static (session, current, _) =>
            {
                current.Touch();
                session.UseDeclaredResource<Payload>("primary", static value => value.Touch());
                session.Canvas.DrawRectangle(s_bounds, session.Fill, session.Pen);
            },
            fill: (Brushes.Resource.Red, Brushes.Resource.Red.Version),
            pen: null,
            brushBounds: s_bounds,
            outputBounds: s_bounds,
            hitTest: RenderHitTestContract.OutputBounds,
            scale: RenderScaleContract.Vector,
            structuralKey: typeof(PrimaryPaintedSourceContractTests))));

        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Disabled);
        KeyNotFoundException? failure =
            Assert.Throws<KeyNotFoundException>(() => renderer.Rasterize());

        TestContext.Out.WriteLine(failure!.Message);
        Assert.Multiple(() =>
        {
            Assert.That(failure.Message, Does.Contain("primary"),
                "the primary lives only in the recorder-owned namespace");
        });
    }

    /// <summary>
    /// The wrapper the recorder builds around the three-argument callback must not become the runtime identity:
    /// the state the author passed still is, so an unchanged second frame is served from the cached output.
    /// </summary>
    [Test]
    public void AnUnchangedSecondFrame_IsServedFromTheCachedOutput()
    {
        using var node = new PrimaryPaintedSourceNode();
        node.Cache.ReportRenderCount(RenderNodeCache.Count);

        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Enabled);
        using (RenderNodeRasterization first = renderer.Rasterize())
        {
            Assert.That(first.IsEmpty, Is.False);
        }

        using RenderNodeRasterization second = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(second.IsEmpty, Is.False);
            Assert.That(node.DrawCount, Is.EqualTo(1),
                "the second frame must not re-invoke the draw callback: the recorder's wrapper is not the "
                + "runtime identity, so the output-cache identity is still the author's state");
        });
    }

    [Test]
    public void RequestLocalPrimary_WithMutableCapturedStateNeverReusesOutputAcrossRequests()
    {
        using var node = new RequestLocalPrimaryPaintedSourceNode();
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Enabled);

        using (RenderNodeRasterization first = renderer.Rasterize())
        {
            Assert.That(GetAlpha(first.Bitmap!, 6, 6), Is.EqualTo(1).Within(0.01f));
        }

        node.DrawBounds = new Rect(0, 0, 4, 4);
        using RenderNodeRasterization second = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(GetAlpha(second.Bitmap!, 6, 6), Is.EqualTo(0).Within(0.01f),
                "the second request must observe the current mutable capture rather than a cached first output");
            Assert.That(node.DrawCount, Is.EqualTo(2));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReusablePaintedSource_UsesDeepStateIdentityInsteadOfAuthorEquality(bool primary)
    {
        using IncompleteEqualityPaintNode node = primary
            ? new PrimaryIncompleteEqualityPaintNode()
            : new RegularIncompleteEqualityPaintNode();
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Enabled);

        using (RenderNodeRasterization first = renderer.Rasterize())
        {
            Assert.That(GetAlpha(first.Bitmap!, 6, 6), Is.EqualTo(1).Within(0.01f));
        }

        node.DrawBounds = new Rect(0, 0, 4, 4);
        using RenderNodeRasterization second = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(GetAlpha(second.Bitmap!, 6, 6), Is.EqualTo(0).Within(0.01f),
                "a pixel field omitted by author equality must still invalidate the painted output");
            Assert.That(node.DrawCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void ANullPrimary_IsRejected()
    {
        ArgumentNullException? rejection = null;
        using var node = new DelegateNode(context =>
        {
            rejection = Assert.Throws<ArgumentNullException>(() => context.PaintedSource(
                primary: (RenderResource<Payload>)null!,
                state: s_bounds,
                draw: static (session, _, state) =>
                    session.Canvas.DrawRectangle(state, session.Fill, session.Pen),
                fill: null,
                pen: null,
                brushBounds: s_bounds,
                outputBounds: s_bounds,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector,
                structuralKey: typeof(PrimaryPaintedSourceContractTests)));
        });

        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Disabled);
        _ = renderer.Measure();

        Assert.That(rejection?.ParamName, Is.EqualTo("primary"));
    }

    [Test]
    public void ACapturingDrawCallback_IsRejected()
    {
        ArgumentException? rejection = null;
        using var node = new DelegateNode(context =>
        {
            var captured = new Payload("captured", []);
            rejection = Assert.Throws<ArgumentException>(() => context.PaintedSource(
                primary: context.Borrow(new Payload("primary", []), cacheKey: "primary", version: 1),
                state: s_bounds,
                draw: (session, _, _) =>
                {
                    captured.Touch();
                    session.Canvas.DrawRectangle(s_bounds, session.Fill, session.Pen);
                },
                fill: null,
                pen: null,
                brushBounds: s_bounds,
                outputBounds: s_bounds,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector,
                structuralKey: typeof(PrimaryPaintedSourceContractTests)));
        });

        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Disabled);
        _ = renderer.Measure();

        Assert.That(rejection?.ParamName, Is.EqualTo("draw"),
            "declaring the primary resource does not relax the state-passing rule on the rest of the callback");
    }

    private static RenderNodeRasterization Rasterize(RenderNode node, RenderCacheOptions cacheOptions)
    {
        using RenderNodeRenderer renderer = CreateRenderer(node, cacheOptions);
        return renderer.Rasterize();
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

    private static float GetAlpha(Bitmap bitmap, int x, int y)
        => (float)bitmap.GetRow<Half>(y)[x * 4 + 3];

    /// <remarks>
    /// The log travels inside the payload rather than beside it, because a static draw callback reaches nothing
    /// but the leased resources and the state.
    /// </remarks>
    private sealed class Payload(string name, List<string> log)
    {
        public int TouchCount { get; private set; }

        public void Touch()
        {
            TouchCount++;
            log.Add(name);
        }
    }

    private sealed class DelegateNode(Action<RenderNodeContext> process) : RenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }

    private sealed class PrimaryPaintedSourceNode : RenderNode
    {
        private readonly DrawCounter _counter = new();

        public int DrawCount => _counter.Count;

        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.ContributeValues(context.PaintedSource(
                primary: context.Borrow(_counter, cacheKey: "counter", version: 1),
                state: s_bounds,
                draw: static (session, counter, _) =>
                {
                    counter.Record();
                    session.Canvas.DrawRectangle(s_bounds, session.Fill, session.Pen);
                },
                fill: (Brushes.Resource.Red, Brushes.Resource.Red.Version),
                pen: null,
                brushBounds: s_bounds,
                outputBounds: s_bounds,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector,
                structuralKey: typeof(PrimaryPaintedSourceNode))));
        }
    }

    private sealed class RequestLocalPrimaryPaintedSourceNode : RenderNode
    {
        private readonly DrawCounter _counter = new();
        private readonly MutableDrawState _state = new() { Bounds = s_bounds };

        public Rect DrawBounds
        {
            get => _state.Bounds;
            set => _state.Bounds = value;
        }

        public int DrawCount => _counter.Count;

        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.ContributeValues(context.PaintedSourceRequestLocal(
                primary: context.Borrow(_counter, cacheKey: "request-local-counter", version: 1),
                draw: (session, counter) =>
                {
                    counter.Record();
                    session.Canvas.DrawRectangle(_state.Bounds, session.Fill, session.Pen);
                },
                fill: (Brushes.Resource.Red, Brushes.Resource.Red.Version),
                pen: null,
                brushBounds: s_bounds,
                outputBounds: s_bounds,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector,
                structuralKey: typeof(RequestLocalPrimaryPaintedSourceNode))));
        }
    }

    private abstract class IncompleteEqualityPaintNode : RenderNode
    {
        protected DrawCounter Counter { get; } = new();

        public Rect DrawBounds { get; set; } = s_bounds;

        public int DrawCount => Counter.Count;

        protected IncompleteEqualityPaintState CreateState()
            => new(s_bounds, DrawBounds);
    }

    private sealed class RegularIncompleteEqualityPaintNode : IncompleteEqualityPaintNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderResource<DrawCounter> counter = context.Borrow(
                Counter,
                cacheKey: typeof(RegularIncompleteEqualityPaintNode),
                version: 1);
            context.Publish(context.ContributeValues(context.PaintedSource(
                state: CreateState(),
                draw: static (session, state) =>
                    session.UseDeclaredResource<DrawCounter>("counter", current =>
                    {
                        current.Record();
                        session.Canvas.DrawRectangle(state.DrawBounds, session.Fill, session.Pen);
                    }),
                fill: (Brushes.Resource.Red, Brushes.Resource.Red.Version),
                pen: null,
                brushBounds: s_bounds,
                outputBounds: s_bounds,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector,
                structuralKey: typeof(RegularIncompleteEqualityPaintNode),
                resources: [counter.Bind("counter")])));
        }
    }

    private sealed class PrimaryIncompleteEqualityPaintNode : IncompleteEqualityPaintNode
    {
        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.ContributeValues(context.PaintedSource(
                primary: context.Borrow(
                    Counter,
                    cacheKey: typeof(PrimaryIncompleteEqualityPaintNode),
                    version: 1),
                state: CreateState(),
                draw: static (session, counter, state) =>
                {
                    counter.Record();
                    session.Canvas.DrawRectangle(state.DrawBounds, session.Fill, session.Pen);
                },
                fill: (Brushes.Resource.Red, Brushes.Resource.Red.Version),
                pen: null,
                brushBounds: s_bounds,
                outputBounds: s_bounds,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector,
                structuralKey: typeof(PrimaryIncompleteEqualityPaintNode))));
        }
    }

    private sealed class DrawCounter
    {
        public int Count { get; private set; }

        public void Record() => Count++;
    }

    private sealed class MutableDrawState
    {
        public Rect Bounds { get; set; }
    }

    private sealed class IncompleteEqualityPaintState(Rect outputBounds, Rect drawBounds)
    {
        public readonly Rect OutputBounds = outputBounds;
        public readonly Rect DrawBounds = drawBounds;

        public override bool Equals(object? obj)
            => obj is IncompleteEqualityPaintState other && OutputBounds == other.OutputBounds;

        public override int GetHashCode() => OutputBounds.GetHashCode();
    }
}
