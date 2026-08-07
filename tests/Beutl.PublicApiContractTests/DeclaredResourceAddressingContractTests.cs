using System.Reflection;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

/// <summary>
/// Pins why a session exposes the token-taking <c>UseResource</c>, the positional
/// <c>UseDeclaredResource</c>, or only one of them.
/// </summary>
/// <remarks>
/// The sanctioned routes to a token are capture and the state tuple, and both are closed on a state-passing
/// callback. A sealed non-tuple state object is the one route that stays open, because the state walk stops at
/// every aggregate that is not a tuple. That is not a hole in the addressing rule but the price list for
/// ignoring it: the state is the produced value's output-cache runtime identity, so a per-frame holder
/// reference makes every lookup miss. A painted source therefore exposes the positional form only by design,
/// not because no token could reach it.
/// </remarks>
[TestFixture]
public sealed class DeclaredResourceAddressingContractTests
{
    private static readonly Rect s_bounds = new(0, 0, 8, 8);

    [Test]
    public void APaintedSourceState_RejectsAResourceTokenInATupleElement()
    {
        ArgumentException? rejection = null;
        using var node = new DelegateSourceNode(context =>
        {
            RenderResource<Payload> token = context.Borrow(new Payload(), cacheKey: "payload");
            rejection = Assert.Throws<ArgumentException>(() => context.PaintedSource(
                state: (s_bounds, token),
                draw: static (session, state) => session.UseDeclaredResource<Payload>(
                    0,
                    _ => session.Canvas.Clear(Colors.Red)),
                fill: null,
                pen: null,
                brushBounds: s_bounds,
                outputBounds: s_bounds,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector,
                structuralKey: typeof(DeclaredResourceAddressingContractTests),
                resources: [token]));
        });

        _ = Measure(node);

        Assert.That(rejection, Is.Not.Null);
        TestContext.Out.WriteLine(rejection!.Message);
        Assert.That(rejection.ParamName, Is.EqualTo("state"),
            "the state is the output-cache runtime identity, so a request-scoped token may not travel in the "
            + "tuple elements the state walk descends through");
    }

    [Test]
    public void APaintedSourceDrawCallback_RejectsAnyCapture()
    {
        ArgumentException? rejection = null;
        using var node = new DelegateSourceNode(context =>
        {
            RenderResource<Payload> token = context.Borrow(new Payload(), cacheKey: "payload");
            rejection = Assert.Throws<ArgumentException>(() => context.PaintedSource(
                state: s_bounds,
                draw: (session, _) => session.UseDeclaredResource<Payload>(
                    IndexOf(token),
                    _ => session.Canvas.Clear(Colors.Red)),
                fill: null,
                pen: null,
                brushBounds: s_bounds,
                outputBounds: s_bounds,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector,
                structuralKey: typeof(DeclaredResourceAddressingContractTests),
                resources: [token]));
        });

        _ = Measure(node);

        Assert.That(rejection, Is.Not.Null);
        TestContext.Out.WriteLine(rejection!.Message);
        Assert.That(rejection.ParamName, Is.EqualTo("draw"),
            "capture is rejected whatever it closes over, so it is not a route to a token either");
    }

    /// <summary>
    /// The route the two rejections above do not close: the state walk descends through tuple elements and
    /// stops at every other aggregate, so a sealed holder carries a token into the draw callback intact.
    /// </summary>
    [Test]
    public void APaintedSourceState_AcceptsASealedHolderCarryingAResourceToken()
    {
        RenderResource<Payload>? recorded = null;
        RenderResource<Payload>? delivered = null;
        using var node = new DelegateSourceNode(context =>
        {
            RenderResource<Payload> token = context.Borrow(new Payload(), cacheKey: "payload");
            recorded = token;
            context.Publish(context.PaintedSource(
                state: new ObservedTokenHolder(token, value => delivered = value),
                draw: static (session, state) =>
                {
                    state.Observe(state.Token);
                    session.Canvas.Clear(Colors.Red);
                },
                fill: null,
                pen: null,
                brushBounds: s_bounds,
                outputBounds: s_bounds,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector,
                structuralKey: typeof(DeclaredResourceAddressingContractTests),
                resources: [token]));
        });

        using RenderNodeRasterization rasterization = Rasterize(node);

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(delivered, Is.SameAs(recorded),
                "the state walk stops at a sealed non-tuple aggregate, so the token reaches the callback");
        });
    }

    /// <summary>
    /// The same holder on a session that does take tokens. The token arrives usable, so the asymmetry between
    /// the two addressing modes cannot rest on a caller being unable to reach the token form.
    /// </summary>
    [Test]
    public void AStatePassingOpaqueCallback_LeasesATokenReachedThroughASealedHolder()
    {
        Payload? leased = null;
        Payload? borrowed = null;
        using var node = new DelegateSourceNode(context =>
        {
            var payload = new Payload();
            borrowed = payload;
            RenderResource<Payload> token = context.Borrow(payload, cacheKey: "payload");
            context.Publish(context.OpaqueSource(OpaqueRenderDescription.Create(
                new LeasedTokenHolder(token, value => leased = value),
                static (session, state) =>
                {
                    session.UseResource(state.Token, state.Lease);
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(static canvas => canvas.Clear(Colors.Red));
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: typeof(DeclaredResourceAddressingContractTests),
                resources: [token])));
        });

        using RenderNodeRasterization rasterization = Rasterize(node);

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(leased, Is.SameAs(borrowed),
                "a state-passing callback can reach and lease a token through the sealed-holder channel");
        });
    }

    /// <summary>
    /// The price of that channel, and the reason positional addressing is the sanctioned one. The state is the
    /// produced value's output-cache runtime identity, and a holder is a fresh reference every recording.
    /// </summary>
    [Test]
    public void ASealedHolderState_CostsEveryOutputCacheHitAValueStateWouldHaveEarned()
    {
        using var holderState = new PaintedSourceNode(carryTheTokenInTheState: true);
        using var valueState = new PaintedSourceNode(carryTheTokenInTheState: false);

        RasterizeTwice(holderState);
        RasterizeTwice(valueState);

        Assert.Multiple(() =>
        {
            Assert.That(valueState.DrawCount, Is.EqualTo(1),
                "a lightweight immutable state compares equal across frames, so the second frame is served "
                + "from the first frame's cached output");
            Assert.That(holderState.DrawCount, Is.EqualTo(2),
                "a holder is a fresh reference every recording, so the runtime identity never matches and the "
                + "cache is defeated for as long as the token travels that way");
        });
    }

    [Test]
    public void EachSession_ExposesTheAddressingModesItsChannelsCanReach()
    {
        (Type Session, bool Token, bool Positional)[] expected =
        [
            (typeof(OpaqueRenderSession), true, true),
            (typeof(GeometrySession), true, true),
            (typeof(TargetScopeSession), true, true),
            (typeof(TargetCommandSession), true, true),
            (typeof(RawTargetScopeSession), true, false),
            (typeof(RawTargetCommandSession), true, false),
            (typeof(PaintedRenderSession), false, true),
        ];

        string[] actual = [.. expected.Select(item => Describe(
            item.Session,
            HasPublicMethod(item.Session, "UseResource"),
            HasPublicMethod(item.Session, "UseDeclaredResource")))];
        string[] pinned = [.. expected.Select(item => Describe(item.Session, item.Token, item.Positional))];

        Assert.That(actual, Is.EqualTo(pinned),
            "a session takes tokens exactly when a capturing channel can reach it, and positions exactly when "
            + "a state-passing one can");
    }

    private static int IndexOf(RenderResource resource)
    {
        _ = resource;
        return 0;
    }

    private static string Describe(Type session, bool token, bool positional)
        => $"{session.Name}: token={token}, positional={positional}";

    private static bool HasPublicMethod(Type session, string name)
        => session.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Any(method => method.Name == name);

    private static RenderNodeMeasurement Measure(RenderNode node)
    {
        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Disabled);
        return renderer.Measure();
    }

    private static RenderNodeRasterization Rasterize(RenderNode node)
    {
        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Disabled);
        return renderer.Rasterize();
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

    private sealed class Payload;

    /// <summary>A sealed non-tuple state object: the aggregate the state walk stops at.</summary>
    private sealed class ObservedTokenHolder(
        RenderResource<Payload> token,
        Action<RenderResource<Payload>> observe)
    {
        public RenderResource<Payload> Token { get; } = token;

        public Action<RenderResource<Payload>> Observe { get; } = observe;
    }

    private sealed class LeasedTokenHolder(RenderResource<Payload> token, Action<Payload> lease)
    {
        public RenderResource<Payload> Token { get; } = token;

        public Action<Payload> Lease { get; } = lease;
    }

    private sealed class DelegateSourceNode(Action<RenderNodeContext> process) : RenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }

    /// <remarks>
    /// The draw counter travels as a declared resource under a key that is equal across frames, so the only
    /// thing that differs between the two variants is whether the state is a value or a holder.
    /// </remarks>
    private sealed class PaintedSourceNode(bool carryTheTokenInTheState) : RenderNode
    {
        private readonly DrawCounter _counter = new();

        public int DrawCount => _counter.Count;

        public override void Process(RenderNodeContext context)
        {
            RenderResource<DrawCounter> counter = context.Borrow(_counter, cacheKey: "counter", version: 1);
            RenderFragmentHandle handle = carryTheTokenInTheState
                ? context.PaintedSource(
                    state: new CountingTokenHolder(counter),
                    draw: static (session, state) => session.UseDeclaredResource<DrawCounter>(
                        0,
                        current => Draw(session, current)),
                    fill: null,
                    pen: null,
                    brushBounds: s_bounds,
                    outputBounds: s_bounds,
                    hitTest: RenderHitTestContract.OutputBounds,
                    scale: RenderScaleContract.Vector,
                    structuralKey: typeof(PaintedSourceNode),
                    resources: [counter])
                : context.PaintedSource(
                    state: s_bounds,
                    draw: static (session, _) => session.UseDeclaredResource<DrawCounter>(
                        0,
                        current => Draw(session, current)),
                    fill: null,
                    pen: null,
                    brushBounds: s_bounds,
                    outputBounds: s_bounds,
                    hitTest: RenderHitTestContract.OutputBounds,
                    scale: RenderScaleContract.Vector,
                    structuralKey: typeof(PaintedSourceNode),
                    resources: [counter]);
            context.Publish(context.ContributeValues(handle));
        }

        private static void Draw(PaintedRenderSession session, DrawCounter counter)
        {
            counter.Record();
            session.Canvas.Clear(Colors.Red);
        }
    }

    private sealed class DrawCounter
    {
        public int Count { get; private set; }

        public void Record() => Count++;
    }

    private sealed class CountingTokenHolder(RenderResource<DrawCounter> token)
    {
        public RenderResource<DrawCounter> Token { get; } = token;
    }
}
