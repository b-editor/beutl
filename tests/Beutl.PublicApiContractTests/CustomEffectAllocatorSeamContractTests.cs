using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class CustomEffectAllocatorSeamContractTests
{
    private static readonly Rect s_domain = new(0, 0, 64, 64);

    [Test]
    public void ANestedPipelineInsideACustomEffect_AllocatesFromTheCallersFactory()
    {
        var factory = new RecordingTargetFactory();
        var probe = new AllocationProbe();

        using (FilterEffect.Resource resource = new ProbeEffect(probe).ToResource(CompositionContext.Default))
        using (FilterEffectRenderNode node = CreateScene(resource))
        using (var renderer = new RenderNodeRenderer(node, CreateRequest(), factory))
        using (renderer.Rasterize())
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(probe.Ran, Is.True, "the custom effect callback has to have executed");
            Assert.That(factory.Created, Is.Not.Empty, "the host's factory has to have been asked for the inputs");

            Assert.That(
                probe.SeamIntermediate,
                Is.Not.Null,
                "an activator minted by the context has to produce a flush buffer");
            Assert.That(
                factory.Created,
                Has.Some.SameAs(probe.SeamIntermediate),
                "the context-minted activator has to allocate its intermediate through the caller's factory, "
                + "so the buffer shares a graphics context with the inputs it draws");

            Assert.That(
                probe.StandaloneIntermediate,
                Is.Not.Null,
                "an activator built through the public constructor still produces a flush buffer");
            Assert.That(
                factory.Created,
                Has.None.SameAs(probe.StandaloneIntermediate),
                "the public constructor documents a standalone activator: it belongs to no render, so it "
                + "self-allocates and this recording factory never sees the request");
        });
    }

    private static RenderNodeRenderRequest CreateRequest()
        => new()
        {
            Intent = RenderIntent.Preview,
            TargetDomain = s_domain,
            OutputScale = 1,
            MaxWorkingScale = 2,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            Purpose = RenderRequestPurpose.Frame,
        };

    private static FilterEffectRenderNode CreateScene(FilterEffect.Resource resource)
    {
        var node = new FilterEffectRenderNode(resource);
        node.AddChild(new EllipseRenderNode(s_domain, Brushes.Resource.White, null));
        return node;
    }

    // A plugin author's effect: it re-applies a Skia chain to the targets it was handed, which is the shape
    // that needs an intermediate of its own.
    [SuppressResourceClassGeneration]
    private sealed partial class ProbeEffect(AllocationProbe probe) : FilterEffect
    {
        public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
            => context.CustomEffect(probe, static (p, ctx) => p.Run(ctx), static (_, bounds) => bounds);

        public override Resource ToResource(CompositionContext context)
        {
            var created = new Resource();
            bool updateOnly = false;
            created.Update(this, context, ref updateOnly);
            return created;
        }

        public new sealed class Resource : FilterEffect.Resource
        {
            public override FilterEffectRenderNode CreateRenderNode() => new(this);
        }
    }

    private sealed class AllocationProbe : IEquatable<AllocationProbe>
    {
        public bool Ran { get; private set; }

        public RenderTarget? SeamIntermediate { get; private set; }

        public RenderTarget? StandaloneIntermediate { get; private set; }

        public void Run(CustomFilterEffectContext context)
        {
            Ran = true;
            SeamIntermediate = FlushOneIntermediate(context, throughSeam: true);
            StandaloneIntermediate = FlushOneIntermediate(context, throughSeam: false);
        }

        private static RenderTarget? FlushOneIntermediate(CustomFilterEffectContext context, bool throughSeam)
        {
            using EffectTargets targets = context.Targets.Clone();
            using var builder = new SKImageFilterBuilder();
            using FilterEffectActivator activator = throughSeam
                ? context.CreateActivator(targets, builder)
                : new FilterEffectActivator(
                    targets,
                    builder,
                    context.Intent,
                    context.Purpose,
                    drawableBrushMaterializer: null,
                    context.OutputScale,
                    context.WorkingScale,
                    context.MaxWorkingScale,
                    context.TargetDomain);

            // A pending Skia filter forces Flush down the allocating path rather than letting it reuse the
            // target it already holds.
            builder.AppendSkiaFilter(
                4f,
                activator,
                static (sigma, input, _) => SKImageFilter.CreateBlur(sigma, sigma, input));
            activator.Flush(false);

            return activator.CurrentTargets.Count > 0
                ? activator.CurrentTargets[0].RenderTarget
                : null;
        }

        public bool Equals(AllocationProbe? other) => ReferenceEquals(this, other);

        public override bool Equals(object? obj) => ReferenceEquals(this, obj);

        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }

    private sealed class RecordingTargetFactory : IRenderTargetFactory
    {
        public List<RenderTarget> Created { get; } = [];

        public RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            RenderTarget? target = RenderTarget.Create(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
            if (target is not null)
                Created.Add(target);

            return target;
        }
    }
}
