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
    private static readonly RenderResourceSlot<Payload> s_leftSlot = new();
    private static readonly RenderResourceSlot<Payload> s_rightSlot = new();
    private static readonly RenderResourceSlot<Payload> s_unboundSlot = new();
    private static readonly RenderResourceSlot<Payload> s_missingSlot = new();

    // Read once here rather than inside the callback: Colors.Red is a get-only property whose getter this
    // compilation cannot see, so a callback naming it is not shown to answer the same way twice.
    private static readonly Color s_fill = Colors.Red;

    private static OpaqueRenderDescription TwoPayloadDescription(
        IReadOnlyList<RenderResourceBinding> bindings)
        => OpaqueRenderDescription.Create(
            (byte)0,
            static (session, _) => session.UseResource(s_leftSlot, left =>
                session.UseResource(s_rightSlot, right =>
                {
                    left.Touch();
                    right.Touch();
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(static canvas => canvas.Clear(s_fill));
                    session.Publish(output);
                })),
            OpaqueRenderBoundsContract.Source(s_bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale,
            resources: bindings,
            slots: [s_leftSlot, s_rightSlot]);

    private static OpaqueRenderDescription MissingLookupDescription(RenderResourceBinding binding)
        => OpaqueRenderDescription.Create(
            (byte)0,
            static (session, _) => session.UseResource(s_missingSlot, static _ => { }),
            OpaqueRenderBoundsContract.Source(s_bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale,
            resources: [binding],
            slots: [s_leftSlot]);

    [TestCase(false)]
    [TestCase(true)]
    public void TypedBindingsRemainStableWhenSameTypedResourcesAreReordered(bool reverse)
    {
        var reached = new List<string>();
        using var node = new DelegateSourceNode(context =>
        {
            RenderResource<Payload> left = context.Borrow(new Payload("left", reached));
            RenderResource<Payload> right = context.Borrow(new Payload("right", reached));
            RenderResourceBinding[] bindings = reverse
                ? [s_rightSlot.Bind(right), s_leftSlot.Bind(left)]
                : [s_leftSlot.Bind(left), s_rightSlot.Bind(right)];
            context.Publish(context.OpaqueSource(TwoPayloadDescription(bindings)));
        });

        using RenderNodeRasterization rasterization = Rasterize(node, RenderCacheOptions.Disabled);

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(reached, Is.EqualTo(new[] { "left", "right" }));
        });
    }

    [Test]
    public void DescriptionsRejectMissingDuplicateAndUnexpectedSlots()
    {
        ArgumentException? missing = null;
        ArgumentException? duplicate = null;
        ArgumentException? unexpected = null;
        using var node = new DelegateSourceNode(context =>
        {
            RenderResource<Payload> first = context.Borrow(new Payload());
            RenderResource<Payload> second = context.Borrow(new Payload());
            missing = Assert.Throws<ArgumentException>(() =>
                TwoPayloadDescription([s_leftSlot.Bind(first)]));
            duplicate = Assert.Throws<ArgumentException>(() =>
                TwoPayloadDescription([s_leftSlot.Bind(first), s_leftSlot.Bind(second)]));
            unexpected = Assert.Throws<ArgumentException>(() =>
                TwoPayloadDescription([s_leftSlot.Bind(first), s_unboundSlot.Bind(second)]));
        });

        _ = Measure(node);

        Assert.Multiple(() =>
        {
            Assert.That(missing!.ParamName, Is.EqualTo("resources"));
            Assert.That(duplicate!.ParamName, Is.EqualTo("resources"));
            Assert.That(unexpected!.ParamName, Is.EqualTo("resources"));
        });
    }

    [Test]
    public void ADescriptionRejectsTheSameSlotTwice()
    {
        ArgumentException? exception = Assert.Throws<ArgumentException>(() =>
            OpaqueRenderDescription.Create(
                (byte)0,
                static (_, _) => { },
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                slots: [s_leftSlot, s_leftSlot]));

        Assert.That(exception!.ParamName, Is.EqualTo("slots"));
    }

    [Test]
    public void MissingSlotFailsWithoutFallingBackToAnotherSameTypedBinding()
    {
        using var node = new DelegateSourceNode(context =>
        {
            RenderResource<Payload> token = context.Borrow(new Payload());
            context.Publish(context.OpaqueSource(
                MissingLookupDescription(s_leftSlot.Bind(token))));
        });
        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Disabled);

        KeyNotFoundException? exception = Assert.Throws<KeyNotFoundException>(() => renderer.Rasterize());

        Assert.That(exception!.Message, Does.Contain("slot"));
    }

    [Test]
    public void PublicResourceAddressingUsesTypedSlotsWithoutCacheIdentityOrNames()
    {
        Type[] slotSessions =
        [
            typeof(OpaqueRenderSession),
            typeof(GeometrySession),
            typeof(TargetScopeSession),
            typeof(TargetCommandSession),
        ];

        MethodInfo? bind = typeof(RenderResourceSlot<Payload>).GetMethod(
            nameof(RenderResourceSlot<Payload>.Bind),
            [typeof(RenderResource<Payload>)]);
        Assert.Multiple(() =>
        {
            Assert.That(bind, Is.Not.Null);
            Assert.That(bind!.ReturnType, Is.EqualTo(typeof(RenderResourceBinding)));
            Assert.That(
                typeof(RenderResourceSlot<Payload>).GetMethod(
                    nameof(RenderResourceSlot<Payload>.Bind),
                    [typeof(RenderResource<OtherPayload>)]),
                Is.Null,
                "A slot can only bind a token of its exact declared resource type.");
            Assert.That(typeof(RenderResourceBinding).GetConstructors(), Is.Empty);
            Assert.That(typeof(RenderResourceBinding).GetProperties(), Is.Empty);
            Assert.That(typeof(RenderResource<Payload>).GetMethod("Bind"), Is.Null);
            Assert.That(typeof(RenderResource).GetProperty("CacheIdentity"), Is.Null);

            foreach (Type session in slotSessions)
            {
                Assert.That(session.GetMethod("UseDeclaredResource"), Is.Null, session.Name);
                MethodInfo[] resourceMethods = session.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(static method => method.Name == "UseResource")
                    .ToArray();
                Assert.That(resourceMethods, Has.Length.EqualTo(1), session.Name);
                ParameterInfo slotParameter = resourceMethods[0].GetParameters()[0];
                Assert.That(slotParameter.ParameterType.IsGenericType, Is.True, session.Name);
                Assert.That(
                    slotParameter.ParameterType.GetGenericTypeDefinition(),
                    Is.EqualTo(typeof(RenderResourceSlot<>)),
                    session.Name);
            }

            Assert.That(
                typeof(RenderNodeContext).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(static method => method.Name is "Own" or "Borrow")
                    .All(static method => method.GetParameters().Length == 1),
                Is.True);
            Assert.That(
                typeof(FilterEffectContext).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(static method => method.Name is "Own" or "Borrow")
                    .All(static method => method.GetParameters().Length == 1),
                Is.True);
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
            new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                TargetDomain = s_bounds,
                CacheOptions = cacheOptions,
                Purpose = RenderRequestPurpose.Frame,
            });

    private sealed class DelegateSourceNode(Action<RenderNodeContext> process) : RenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }

    private sealed class Payload(string? name = null, List<string>? reached = null)
    {
        public void Touch()
        {
            if (name is not null)
                reached!.Add(name);
        }
    }

    private sealed class OtherPayload;
}
