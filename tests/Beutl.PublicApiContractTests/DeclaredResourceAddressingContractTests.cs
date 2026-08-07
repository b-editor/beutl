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
/// The state is the produced value's output-cache runtime identity. A resource token in a tuple element is
/// rejected, and a capturing callback is rejected. A sealed non-tuple state does pass validation and physically
/// delivers a token, but it is an enumerated identity channel rather than a way to address resources: the
/// author then owns the identity contract by hand. A holder allocated per recording loses output-cache reuse;
/// a reused or value-equal holder keeps reuse, but the identity then tracks only what that holder's equality
/// compares, so a pixel-affecting change covered by neither it nor a declared resource's version is served from
/// a stale cached output; and a token left over from a finished request throws when leased. Position is the
/// address by design, not by impossibility.
/// <para>
/// Every holder-shaped state below leases the token it carries. Reaching the same resource by position instead
/// would make these tests re-pin the mutable-state-holder channel
/// <c>RenderCacheIdentityChannelTests.MutableObjectReferencedByState_IsServedStale</c> already covers, rather
/// than the token channel they are named for.
/// </para>
/// </remarks>
[TestFixture]
public sealed class DeclaredResourceAddressingContractTests
{
    private static readonly Rect s_bounds = new(0, 0, 8, 8);

    private static RenderResource<Payload>? s_staticToken;

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
    /// The three description factories the in-tree index-0 sites record through reject every route to a token
    /// that they validate: a callback that captures it, and a token carried in a <c>state</c> tuple element.
    /// </summary>
    [Test]
    public void TheThreeDescriptionFactories_RejectEveryValidatedRouteToAToken()
    {
        var rejections = new List<string>();
        using var node = new DelegateSourceNode(context =>
        {
            RenderResource<Payload> token = context.Borrow(new Payload(), cacheKey: "payload");

            rejections.Add(Reject(() => OpaqueRenderDescription.Create(
                s_bounds,
                (session, _) => session.UseResource(token, static _ => { }),
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: "captured-opaque",
                resources: [token])));
            rejections.Add(Reject(() => TargetScopeDescription.Create(
                s_bounds,
                (session, _) => session.UseResource(token, static _ => { }),
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput,
                RenderScaleContract.PreserveInputSupply,
                RenderDeviceGridMapping.Preserved,
                structuralKey: "captured-scope",
                resources: [token])));
            rejections.Add(Reject(() => TargetCommandDescription.Create(
                s_bounds,
                (session, _) => session.UseResource(token, static _ => { }),
                TargetRegion.Region(s_bounds),
                s_bounds,
                RenderHitTestContract.None,
                structuralKey: "captured-command",
                resources: [token])));

            rejections.Add(Reject(() => OpaqueRenderDescription.Create(
                (s_bounds, token),
                static (session, state) => session.UseResource(state.token, static _ => { }),
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: "tupled-opaque",
                resources: [token])));
            rejections.Add(Reject(() => TargetScopeDescription.Create(
                (s_bounds, token),
                static (session, state) => session.UseResource(state.token, static _ => { }),
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput,
                RenderScaleContract.PreserveInputSupply,
                RenderDeviceGridMapping.Preserved,
                structuralKey: "tupled-scope",
                resources: [token])));
            rejections.Add(Reject(() => TargetCommandDescription.Create(
                (s_bounds, token),
                static (session, state) => session.UseResource(state.token, static _ => { }),
                TargetRegion.Region(s_bounds),
                s_bounds,
                RenderHitTestContract.None,
                structuralKey: "tupled-command",
                resources: [token])));
        });

        _ = Measure(node);

        foreach (string rejection in rejections)
            TestContext.Out.WriteLine(rejection);

        Assert.That(rejections, Is.EqualTo(new[] { "execute", "execute", "execute", "state", "state", "state" }),
            "capture is rejected on all three, and so is a token in a state tuple element, so no route these "
            + "factories validate reaches the token form from a state-passing callback");
    }

    /// <summary>
    /// Why the rejections above do not make the token form unreachable, and why the sealed holder is not the
    /// only way past them. A <c>static</c> field is outside everything the state rules look at.
    /// </summary>
    [Test]
    public void AStaticFieldReadByAStaticCallback_AlsoLeasesAToken()
    {
        Payload? leased = null;
        Payload? borrowed = null;
        using var node = new DelegateSourceNode(context =>
        {
            var payload = new Payload();
            borrowed = payload;
            RenderResource<Payload> token = context.Borrow(payload, cacheKey: "payload");
            s_staticToken = token;
            context.Publish(context.OpaqueSource(OpaqueRenderDescription.Create(
                new LeasedTokenSink(value => leased = value),
                static (session, sink) =>
                {
                    session.UseResource(s_staticToken!, sink.Lease);
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
                "a static field is not part of state, so nothing validates it and the token arrives usable");
        });
    }

    /// <summary>
    /// The compile-time-safe route to the token that the rejections leave open, and what it costs. Recording
    /// through <c>CreateRequestLocal</c> lets the callback capture the token outright, and buys that with a
    /// fresh request-local identity every recording, so the result is never served from the output cache.
    /// </summary>
    [TestCase(true, 2)]
    [TestCase(false, 1)]
    public void CapturingATokenRequiresARequestLocalRecording_WhichGivesUpTheOutputCacheHit(
        bool requestLocal,
        int expectedExecutions)
    {
        var counter = new DrawCounter();
        using var node = new DelegateSourceNode(context =>
        {
            RenderResource<DrawCounter> token = context.Borrow(counter, cacheKey: "counter", version: 1);
            OpaqueRenderDescription description = requestLocal
                ? OpaqueRenderDescription.CreateRequestLocal(
                    session => session.UseResource(token, current => DrawAndCount(session, current)),
                    OpaqueRenderBoundsContract.Source(s_bounds),
                    RenderHitTestContract.OutputBounds,
                    RenderValueCardinality.Single,
                    RenderScaleContract.MaterializeAtWorkingScale,
                    structuralKey: typeof(DeclaredResourceAddressingContractTests),
                    resources: [token])
                : OpaqueRenderDescription.Create(
                    s_bounds,
                    static (session, _) => session.UseDeclaredResource<DrawCounter>(
                        0,
                        current => DrawAndCount(session, current)),
                    OpaqueRenderBoundsContract.Source(s_bounds),
                    RenderHitTestContract.OutputBounds,
                    RenderValueCardinality.Single,
                    RenderScaleContract.MaterializeAtWorkingScale,
                    structuralKey: typeof(DeclaredResourceAddressingContractTests),
                    resources: [token]);
            context.Publish(context.ContributeValues(context.OpaqueSource(description)));
        });

        RasterizeTwice(node);

        Assert.That(counter.Count, Is.EqualTo(expectedExecutions),
            "a request-local recording is the one route to a captured token that nothing has to be trusted "
            + "for, and its price is that an unchanged second frame re-executes instead of being served");
    }

    /// <summary>
    /// One of the two ways of holding a holder, and the only one the fixture used to state as a general fact.
    /// A node that allocates its holder inside <c>Process</c> hands the description a fresh reference every
    /// recording, so its runtime identity cannot match the previous frame's.
    /// </summary>
    [Test]
    public void AHolderAllocatedPerRecording_LosesTheOutputCacheReuseAValueStateKeeps()
    {
        using var holderState = new StateShapeSourceNode(StateShape.HolderPerRecording);
        using var valueState = new StateShapeSourceNode(StateShape.Value);

        RasterizeTwice(holderState);
        RasterizeTwice(valueState);

        Assert.Multiple(() =>
        {
            Assert.That(valueState.DrawCount, Is.EqualTo(1),
                "a lightweight immutable state compares equal across frames, so the second frame is served "
                + "from the first frame's cached output");
            Assert.That(holderState.DrawCount, Is.EqualTo(2),
                "this node allocates its holder inside Process, so the identity it publishes is a fresh "
                + "reference every recording. That is a property of how this node holds the holder, not of "
                + "carrying a token in one — see AReusedHolderAndAValueEqualHolder_KeepTheOutputCacheHit");
        });
    }

    /// <summary>
    /// The other two ways, and the reason the cost of this channel cannot be stated as a lost cache hit. Both
    /// of these states carry a resource token and both are served from the cached output.
    /// </summary>
    [Test]
    public void AReusedHolderAndAValueEqualHolder_KeepTheOutputCacheHit()
    {
        using var reused = new StateShapeSourceNode(StateShape.HolderReusedAcrossRecordings);
        using var valueEqual = new StateShapeSourceNode(StateShape.ValueEqualHolderPerRecording);

        RasterizeTwice(reused);
        RasterizeTwice(valueEqual);

        Assert.Multiple(() =>
        {
            Assert.That(reused.DrawCount, Is.EqualTo(1),
                "one holder reused across recordings, with its token refreshed each Process, is the same "
                + "reference every frame, so the second frame is served from the cached output");
            Assert.That(valueEqual.DrawCount, Is.EqualTo(1),
                "a holder whose Equals/GetHashCode compare by value is equal to the previous frame's holder, "
                + "so the second frame is served from the cached output while still carrying a token");
        });
    }

    /// <summary>
    /// What that retained hit actually costs, and the part that matters most with the render cache enabled: the
    /// holder's identity no longer tracks what the callback draws with, so a pixel-affecting change is served
    /// from a stale cached output and is silently dropped.
    /// </summary>
    [Test]
    public void AReusedHolderState_ServesStalePixelsWhenItsCarriedValueChanges()
    {
        using var node = new MutatingHolderStateNode();
        node.Cache.ReportRenderCount(RenderNodeCache.Count);

        ulong first;
        ulong second;
        using (RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Enabled))
        {
            using (RenderNodeRasterization frame = renderer.Rasterize())
            {
                first = FirstPixel(frame);
            }

            node.Color = Colors.Lime;
            using (RenderNodeRasterization frame = renderer.Rasterize())
            {
                second = FirstPixel(frame);
            }
        }

        Color[] drawnUnderTheCache = [.. node.DrawnColors];

        ulong withoutTheCache;
        using (RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Disabled))
        using (RenderNodeRasterization frame = renderer.Rasterize())
        {
            withoutTheCache = FirstPixel(frame);
        }

        TestContext.Out.WriteLine($"drawn under the cache: [{string.Join(", ", drawnUnderTheCache)}]");
        Assert.Multiple(() =>
        {
            Assert.That(drawnUnderTheCache, Is.EqualTo(new[] { Colors.Red }),
                "the second frame never re-ran the draw callback, so the changed colour was never drawn");
            Assert.That(second, Is.EqualTo(first),
                "the second frame is the first frame's cached pixels, served after the change");
            Assert.That(withoutTheCache, Is.Not.EqualTo(first),
                "the change was pixel-affecting, so those cached pixels are wrong and not merely identical");
        });
    }

    /// <summary>
    /// Why the reused holder above refreshes its token every <c>Process</c>. A holder that keeps the token an
    /// earlier request gave it hands the callback a released one, and leasing that is a hard failure rather
    /// than a stale read.
    /// </summary>
    /// <remarks>
    /// The failure is reached only when the callback runs, so the second assertion is the part that matters
    /// with the render cache enabled: an unchanged holder is served from the cached output, and the stale token
    /// stays latent until the first execution that actually leases it.
    /// </remarks>
    [Test]
    public void ATokenLeftOverFromAFinishedRequest_ThrowsWhenLeased()
    {
        using var thrown = new StaleTokenNode();
        InvalidOperationException? failure;
        using (RenderNodeRenderer renderer = CreateRenderer(thrown, RenderCacheOptions.Disabled))
        {
            using (RenderNodeRasterization first = renderer.Rasterize())
            {
                Assert.That(first.IsEmpty, Is.False);
            }

            failure = Assert.Throws<InvalidOperationException>(() => renderer.Rasterize());
        }

        using var latent = new StaleTokenNode();
        latent.Cache.ReportRenderCount(RenderNodeCache.Count);
        Exception? underTheCache;
        using (RenderNodeRenderer cached = CreateRenderer(latent, RenderCacheOptions.Enabled))
        {
            using (RenderNodeRasterization first = cached.Rasterize())
            {
                Assert.That(first.IsEmpty, Is.False);
            }

            try
            {
                using RenderNodeRasterization second = cached.Rasterize();
                underTheCache = null;
            }
            catch (Exception ex)
            {
                underTheCache = ex;
            }
        }

        TestContext.Out.WriteLine(failure!.Message);
        Assert.Multiple(() =>
        {
            Assert.That(failure.Message, Does.Contain("no longer retains its request-scoped slot"),
                "the second request's callback leased the first request's token");
            Assert.That(underTheCache, Is.Null,
                "an unchanged holder is served from the cached output, so the stale token is never leased");
            Assert.That(latent.LeaseCount, Is.EqualTo(1));
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

    private static void DrawAndCount(OpaqueRenderSession session, DrawCounter counter)
    {
        counter.Record();
        using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
        output.Canvas.Use(static canvas => canvas.Clear(Colors.Red));
        session.Publish(output);
    }

    private static string Reject(Func<object> create)
    {
        ArgumentException? rejection = Assert.Throws<ArgumentException>(() => create());
        TestContext.Out.WriteLine(rejection!.Message);
        return rejection.ParamName!;
    }

    private static string Describe(Type session, bool token, bool positional)
        => $"{session.Name}: token={token}, positional={positional}";

    private static bool HasPublicMethod(Type session, string name)
        => session.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Any(method => method.Name == name);

    private static ulong FirstPixel(RenderNodeRasterization rasterization)
    {
        Assert.That(rasterization.IsEmpty, Is.False);
        ReadOnlySpan<ushort> pixels = rasterization.Bitmap!.GetPixelSpan<ushort>();
        return ((ulong)pixels[0] << 48)
               | ((ulong)pixels[1] << 32)
               | ((ulong)pixels[2] << 16)
               | pixels[3];
    }

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

    private sealed class LeasedTokenSink(Action<Payload> lease)
    {
        public Action<Payload> Lease { get; } = lease;
    }

    private sealed class DelegateSourceNode(Action<RenderNodeContext> process) : RenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }

    public enum StateShape
    {
        Value,
        HolderPerRecording,
        HolderReusedAcrossRecordings,
        ValueEqualHolderPerRecording,
    }

    private interface ITokenHolder
    {
        RenderResource<DrawCounter>? Token { get; }
    }

    /// <remarks>
    /// The draw counter travels as a declared resource under a key that is equal across frames, so the only
    /// thing that differs between the variants is the shape of the state. Each holder shape leases the token it
    /// carries, which is the whole point of carrying one; the value shape has no token to lease and addresses
    /// the same single declared resource by position.
    /// </remarks>
    private sealed class StateShapeSourceNode(StateShape shape) : RenderNode
    {
        private readonly DrawCounter _counter = new();
        private readonly ReusedTokenHolder _reused = new();

        public int DrawCount => _counter.Count;

        public override void Process(RenderNodeContext context)
        {
            RenderResource<DrawCounter> counter = context.Borrow(_counter, cacheKey: "counter", version: 1);
            RenderFragmentHandle handle = shape switch
            {
                StateShape.Value => context.OpaqueSource(Describe(
                    s_bounds,
                    static (session, _) => session.UseDeclaredResource<DrawCounter>(
                        0,
                        current => Draw(session, current)),
                    counter)),
                StateShape.HolderPerRecording =>
                    RecordHolder(context, new CountingTokenHolder(counter), counter),
                StateShape.HolderReusedAcrossRecordings =>
                    RecordHolder(context, _reused.Refresh(counter), counter),
                _ => RecordHolder(context, new ValueEqualTokenHolder(counter, s_bounds), counter),
            };
            context.Publish(context.ContributeValues(handle));
        }

        private static RenderFragmentHandle RecordHolder<TState>(
            RenderNodeContext context,
            TState state,
            RenderResource<DrawCounter> counter)
            where TState : class, ITokenHolder
            => context.OpaqueSource(Describe(
                state,
                static (session, state) => session.UseResource(
                    state.Token!,
                    current => Draw(session, current)),
                counter));

        private static OpaqueRenderDescription Describe<TState>(
            TState state,
            Action<OpaqueRenderSession, TState> execute,
            RenderResource<DrawCounter> counter)
            where TState : notnull
            => OpaqueRenderDescription.Create(
                state,
                execute,
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: typeof(StateShapeSourceNode),
                resources: [counter]);

        private static void Draw(OpaqueRenderSession session, DrawCounter counter)
        {
            counter.Record();
            using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
            output.Canvas.Use(static canvas => canvas.Clear(Colors.Red));
            session.Publish(output);
        }
    }

    /// <remarks>
    /// The shape a plugin author reaches for once the per-recording holder's lost cache hits show up in a
    /// profile: one holder, kept alive by the node, with the request-scoped token refreshed every
    /// <c>Process</c> because the previous request's token would throw when leased.
    /// </remarks>
    private sealed class MutatingHolderStateNode : RenderNode
    {
        private readonly DrawLog _log = new();
        private readonly MutableHolder _holder = new();

        public Color Color
        {
            get => _holder.Color;
            set => _holder.Color = value;
        }

        public IReadOnlyList<Color> DrawnColors => _log.Colors;

        public override void Process(RenderNodeContext context)
        {
            RenderResource<DrawLog> log = context.Borrow(_log, cacheKey: "log", version: 1);
            _holder.Token = log;
            context.Publish(context.ContributeValues(context.OpaqueSource(OpaqueRenderDescription.Create(
                _holder,
                static (session, state) => session.UseResource(
                    state.Token!,
                    current =>
                    {
                        current.Record(state.Color);
                        using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                        output.Canvas.Use(canvas => canvas.Clear(state.Color));
                        session.Publish(output);
                    }),
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: typeof(MutatingHolderStateNode),
                resources: [log]))));
        }
    }

    /// <remarks>
    /// The token is captured on the first <c>Process</c> and never refreshed, which is the one thing the
    /// reused-holder shape in <see cref="StateShapeSourceNode"/> does do.
    /// </remarks>
    private sealed class StaleTokenNode : RenderNode
    {
        private readonly DrawCounter _counter = new();
        private readonly ReusedTokenHolder _holder = new();

        public int LeaseCount => _counter.Count;

        public override void Process(RenderNodeContext context)
        {
            RenderResource<DrawCounter> counter = context.Borrow(_counter, cacheKey: "counter", version: 1);
            if (_holder.Token is null)
                _holder.Refresh(counter);

            context.Publish(context.ContributeValues(context.OpaqueSource(OpaqueRenderDescription.Create(
                _holder,
                static (session, state) =>
                {
                    session.UseResource(state.Token!, static current => current.Record());
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(static canvas => canvas.Clear(Colors.Red));
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: typeof(StaleTokenNode),
                resources: [counter]))));
        }
    }

    private sealed class DrawCounter
    {
        public int Count { get; private set; }

        public void Record() => Count++;
    }

    private sealed class DrawLog
    {
        private readonly List<Color> _colors = [];

        public IReadOnlyList<Color> Colors => _colors;

        public void Record(Color color) => _colors.Add(color);
    }

    private sealed class CountingTokenHolder(RenderResource<DrawCounter> token) : ITokenHolder
    {
        public RenderResource<DrawCounter>? Token { get; } = token;
    }

    private sealed class ReusedTokenHolder : ITokenHolder
    {
        public RenderResource<DrawCounter>? Token { get; private set; }

        public ReusedTokenHolder Refresh(RenderResource<DrawCounter> token)
        {
            Token = token;
            return this;
        }
    }

    private sealed class ValueEqualTokenHolder(RenderResource<DrawCounter> token, Rect bounds) : ITokenHolder
    {
        public RenderResource<DrawCounter>? Token { get; } = token;

        public override bool Equals(object? obj) => obj is ValueEqualTokenHolder other && other.Bounds == Bounds;

        public override int GetHashCode() => Bounds.GetHashCode();

        private Rect Bounds { get; } = bounds;
    }

    private sealed class MutableHolder
    {
        public Color Color { get; set; } = Colors.Red;

        public RenderResource<DrawLog>? Token { get; set; }
    }
}
