using System.Reflection;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class DeclaredResourceAddressingContractTests
{
    private static readonly Rect s_bounds = new(0, 0, 8, 8);

    [TestCase(false)]
    [TestCase(true)]
    public void NamedBindingsRemainStableWhenSameTypedResourcesAreReordered(bool reverse)
    {
        var reached = new List<string>();
        using var node = new DelegateSourceNode(context =>
        {
            RenderResource<Payload> left = context.Borrow(
                new Payload("left", reached),
                cacheKey: "left",
                version: 1);
            RenderResource<Payload> right = context.Borrow(
                new Payload("right", reached),
                cacheKey: "right",
                version: 1);
            RenderResourceBinding[] bindings = reverse
                ? [right.Bind("right"), left.Bind("left")]
                : [left.Bind("left"), right.Bind("right")];
            context.Publish(context.OpaqueSource(OpaqueRenderDescription.Create(
                s_bounds,
                static (session, _) => session.UseDeclaredResource<Payload>("left", leftPayload =>
                    session.UseDeclaredResource<Payload>("right", rightPayload =>
                    {
                        leftPayload.Touch();
                        rightPayload.Touch();
                        using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                        output.Canvas.Use(static canvas => canvas.Clear(Colors.Red));
                        session.Publish(output);
                    })),
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                resources: bindings)));
        });

        using RenderNodeRasterization rasterization = Rasterize(node, RenderCacheOptions.Disabled);

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(reached, Is.EqualTo(new[] { "left", "right" }));
        });
    }

    [Test]
    public void BindingValidationRejectsBlankAndDuplicateNames()
    {
        ArgumentException? blank = null;
        ArgumentException? duplicate = null;
        using var node = new DelegateSourceNode(context =>
        {
            RenderResource<Payload> first = context.Borrow(new Payload(), cacheKey: "first");
            RenderResource<Payload> second = context.Borrow(new Payload(), cacheKey: "second");
            blank = Assert.Throws<ArgumentException>(() => first.Bind(" "));
            duplicate = Assert.Throws<ArgumentException>(() => OpaqueRenderDescription.Create(
                s_bounds,
                static (_, _) => { },
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.Vector,
                resources: [first.Bind("payload"), second.Bind("payload")]));
        });

        _ = Measure(node);

        Assert.Multiple(() =>
        {
            Assert.That(blank!.ParamName, Is.EqualTo("name"));
            Assert.That(duplicate!.ParamName, Is.EqualTo("resources"));
        });
    }

    [TestCase(ResourceFailure.MissingName, typeof(KeyNotFoundException), "missing")]
    [TestCase(ResourceFailure.TypeMismatch, typeof(InvalidOperationException), "OtherPayload")]
    public void NamedLookupFailsDeterministically(
        ResourceFailure failure,
        Type exceptionType,
        string messageFragment)
    {
        using var node = new LookupFailureNode(failure);
        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Disabled);

        Exception? exception = Assert.Throws(exceptionType, () => renderer.Rasterize());

        Assert.That(exception!.Message, Does.Contain(messageFragment));
    }

    [Test]
    public void BindingIdentityInvalidatesTheOutputCacheWhenTheVersionChanges()
    {
        using var node = new VersionedPayloadNode();
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Enabled);

        using (RenderNodeRasterization first = renderer.Rasterize())
        using (RenderNodeRasterization second = renderer.Rasterize())
        {
            Assert.That(first.IsEmpty || second.IsEmpty, Is.False);
        }

        node.Version++;
        using RenderNodeRasterization third = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(third.IsEmpty, Is.False);
            Assert.That(node.ExecutionCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void CallbackRuntimeIdentity_IsNotAPublicAuthoringChannel()
    {
        Type[] descriptions =
        [
            typeof(OpaqueRenderDescription),
            typeof(GeometryDescription),
            typeof(TargetScopeDescription),
            typeof(TargetCommandDescription),
        ];

        Assert.Multiple(() =>
        {
            Assert.That(
                typeof(OpaqueRenderDescription).Assembly.GetExportedTypes()
                    .Any(static type => type.FullName == "Beutl.Graphics.Rendering.RenderRuntimeIdentity"),
                Is.False);
            foreach (Type description in descriptions)
            {
                Assert.That(
                    description.GetProperty("RuntimeIdentity", BindingFlags.Public | BindingFlags.Instance),
                    Is.Null,
                    description.FullName);
            }
        });
    }

    [Test]
    public void EveryDeclaredResourceSessionUsesAStringNameAndRawSessionsRemainTokenOnly()
    {
        Type[] namedSessions =
        [
            typeof(OpaqueRenderSession),
            typeof(GeometrySession),
            typeof(TargetScopeSession),
            typeof(TargetCommandSession),
        ];
        foreach (Type session in namedSessions)
        {
            MethodInfo? method = session.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .SingleOrDefault(static candidate => candidate.Name == "UseDeclaredResource");
            Assert.That(method, Is.Not.Null, session.Name);
            Assert.That(method!.GetParameters()[0].ParameterType, Is.EqualTo(typeof(string)), session.Name);
        }

        Assert.Multiple(() =>
        {
            Assert.That(typeof(RenderResourceBinding).GetConstructors(), Is.Empty);
            Assert.That(typeof(RawTargetScopeSession).GetMethod("UseDeclaredResource"), Is.Null);
            Assert.That(typeof(RawTargetCommandSession).GetMethod("UseDeclaredResource"), Is.Null);
            Assert.That(typeof(RawTargetScopeSession).GetMethod("UseResource"), Is.Not.Null);
            Assert.That(typeof(RawTargetCommandSession).GetMethod("UseResource"), Is.Not.Null);
        });
    }

    private static RenderNodeMeasurement Measure(RenderNode node)
    {
        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Disabled);
        return renderer.Measure();
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

    public enum ResourceFailure
    {
        MissingName,
        TypeMismatch,
    }

    private sealed class DelegateSourceNode(Action<RenderNodeContext> process) : RenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }

    private sealed class LookupFailureNode(ResourceFailure failure) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderResource<Payload> token = context.Borrow(new Payload(), cacheKey: "payload");
            context.Publish(context.OpaqueSource(OpaqueRenderDescription.Create(
                failure,
                static (session, currentFailure) =>
                {
                    if (currentFailure == ResourceFailure.MissingName)
                    {
                        session.UseDeclaredResource<Payload>("missing", static _ => { });
                    }
                    else
                    {
                        session.UseDeclaredResource<OtherPayload>("payload", static _ => { });
                    }
                },
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                resources: [token.Bind("payload")])));
        }
    }

    private sealed class VersionedPayloadNode : RenderNode
    {
        private readonly Payload _payload = new();

        public int Version { get; set; } = 1;

        public int ExecutionCount => _payload.Count;

        public override void Process(RenderNodeContext context)
        {
            RenderResource<Payload> token = context.Borrow(
                _payload,
                cacheKey: "versioned-payload",
                version: Version);
            context.Publish(context.OpaqueSource(OpaqueRenderDescription.Create(
                s_bounds,
                static (session, _) => session.UseDeclaredResource<Payload>("payload", payload =>
                {
                    payload.Touch();
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(static canvas => canvas.Clear(Colors.Red));
                    session.Publish(output);
                }),
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                resources: [token.Bind("payload")])));
        }
    }

    private sealed class Payload(string? name = null, List<string>? reached = null)
    {
        public int Count { get; private set; }

        public void Touch()
        {
            Count++;
            if (name is not null)
                reached!.Add(name);
        }
    }

    private sealed class OtherPayload;
}
