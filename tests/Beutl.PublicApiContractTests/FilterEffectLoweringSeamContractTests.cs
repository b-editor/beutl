using System.Buffers.Binary;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.PublicApiContractTests;

/// <remarks>
/// This assembly is deliberately not an InternalsVisibleTo friend of Beutl.Engine, so everything these tests
/// touch is reachable by an out-of-tree plugin.
/// </remarks>
[TestFixture]
public sealed class FilterEffectLoweringSeamContractTests
{
    private static readonly Rect s_bounds = new(3, 5, 12, 8);

    [Test]
    public void PublishLoweredResultOverride_ReceivesTheLoweredFragmentsAndItsOwnScopeReachesTheOutput()
    {
        using FilterEffect.Resource baselineResource = CreateIdentityEffectResource();
        using FilterEffect.Resource scopedResource = CreateIdentityEffectResource();
        var scopedFilter = new ScopePublishingFilterNode(scopedResource);

        using RenderNode baseline = CreateEffectNode(
            baselineResource.CreateRenderNode(),
            new SolidSourceNode(s_bounds, Colors.CornflowerBlue));
        using RenderNode scoped = CreateEffectNode(
            scopedFilter,
            new SolidSourceNode(s_bounds, Colors.CornflowerBlue));

        using RenderNodeRasterization baselineRasterization = Rasterize(baseline);
        using RenderNodeRasterization scopedRasterization = Rasterize(scoped);

        Assert.Multiple(() =>
        {
            Assert.That(scopedFilter.PublishCalls, Is.EqualTo(1),
                "the seam must be the single publication point of the lowered result");
            Assert.That(scopedFilter.LastLoweredCount, Is.GreaterThan(0),
                "the seam must receive the fragments the effect lowered to, not an empty list");
            Assert.That(scopedFilter.LastLoweredBounds, Is.Not.EqualTo(Rect.Empty),
                "a lowered fragment must carry recorded metadata the subclass can scope against");
            Assert.That(scopedRasterization.IsEmpty, Is.False,
                "wrapping the lowered result in the subclass's own target scope must still render it");
            Assert.That(scopedRasterization.Bitmap, Is.Not.Null);
            Assert.That(baselineRasterization.Bitmap, Is.Not.Null);
            AssertBitmapsEqual(baselineRasterization.Bitmap!, scopedRasterization.Bitmap!);
        });
    }

    [Test]
    public void RequiresInputIsolationOverride_IsAskedForEveryInputAndWideningItPreservesTheRenderedResult()
    {
        using FilterEffect.Resource baselineResource = CreateIdentityEffectResource();
        using FilterEffect.Resource isolatingResource = CreateIdentityEffectResource();
        var isolatingFilter = new AlwaysIsolatingFilterNode(isolatingResource);

        using RenderNode baseline = CreateEffectNode(
            baselineResource.CreateRenderNode(),
            new SolidSourceNode(s_bounds, Colors.CornflowerBlue));
        using RenderNode isolating = CreateEffectNode(
            isolatingFilter,
            new SolidSourceNode(s_bounds, Colors.CornflowerBlue));

        using RenderNodeRasterization baselineRasterization = Rasterize(baseline);
        using RenderNodeRasterization isolatingRasterization = Rasterize(isolating);

        Assert.Multiple(() =>
        {
            Assert.That(isolatingFilter.IsolationQueries, Is.GreaterThan(0),
                "the predicate must decide isolation, so it has to be asked about the inputs");
            Assert.That(isolatingFilter.IsolateCalls, Is.EqualTo(1),
                "answering true must route the inputs through the isolation seam exactly once");
            Assert.That(isolatingFilter.LastDomainWasFinite, Is.True,
                "a concrete value-only input resolves a finite isolation domain rather than Rect.Invalid");
            Assert.That(isolatingRasterization.IsEmpty, Is.False);
            Assert.That(isolatingRasterization.Bitmap, Is.Not.Null);
            Assert.That(baselineRasterization.Bitmap, Is.Not.Null);
            AssertBitmapsEqual(baselineRasterization.Bitmap!, isolatingRasterization.Bitmap!);
        });
    }

    [Test]
    public void HasSymbolicInputTargetWrite_DistinguishesAFullTargetWriteFromValueOnlyInputs()
    {
        var counter = new ExecutionCounter();
        using var valueOnly = new PluginContentIsolationNode();
        valueOnly.AddChild(new SolidSourceNode(s_bounds, Colors.CornflowerBlue));
        using var symbolicWrite = new PluginContentIsolationNode();
        symbolicWrite.AddChild(new RawTargetWriteNode(counter));

        Measure(valueOnly);
        Measure(symbolicWrite);

        Assert.Multiple(() =>
        {
            Assert.That(valueOnly.ObservedSymbolicTargetWrite, Is.False,
                "an input that only contributes values is fully described by its recorded bounds");
            Assert.That(valueOnly.ObservedInputBounds, Is.EqualTo(s_bounds),
                "the recorded-bounds hint must describe the value input the scope is about to bound");
            Assert.That(symbolicWrite.ObservedSymbolicTargetWrite, Is.True,
                "a raw target command writes pixels no recorded value bounds describe");
            Assert.That(symbolicWrite.ObservedInputBounds, Is.EqualTo(Rect.Empty),
                "the write contributes no value bounds, so scoping by them alone would empty the scope");
        });
    }

    [Test]
    public void TryCalculateFiniteIsolationDomain_ResolvesValueInputsAndRefusesASymbolicTargetWrite()
    {
        var counter = new ExecutionCounter();
        using var valueOnly = new PluginIsolatingContainerNode();
        valueOnly.AddChild(new SolidSourceNode(s_bounds, Colors.CornflowerBlue));
        using var symbolicWrite = new PluginIsolatingContainerNode();
        symbolicWrite.AddChild(new RawTargetWriteNode(counter));

        Measure(valueOnly);
        Measure(symbolicWrite);

        Assert.Multiple(() =>
        {
            Assert.That(valueOnly.ResolvedFiniteDomain, Is.True,
                "a concrete value input bounds the domain a finite Layer needs");
            Assert.That(valueOnly.ObservedDomain, Is.EqualTo(s_bounds));
            Assert.That(symbolicWrite.ResolvedFiniteDomain, Is.False,
                "an unresolved target write leaves no finite domain, so the node must defer to its owning target");
        });
    }

    [Test]
    public void DefaultLoweringSeams_RenderIdenticallyToOverridesThatRestateTheBaseBehaviour()
    {
        using FilterEffect.Resource stockResource = CreateIdentityEffectResource();
        using FilterEffect.Resource restatedResource = CreateIdentityEffectResource();

        using RenderNode unfiltered = new SolidSourceNode(s_bounds, Colors.CornflowerBlue);
        using RenderNode stock = CreateEffectNode(
            stockResource.CreateRenderNode(),
            new SolidSourceNode(s_bounds, Colors.CornflowerBlue));
        using RenderNode restated = CreateEffectNode(
            new ExplicitBaseBehaviourFilterNode(restatedResource),
            new SolidSourceNode(s_bounds, Colors.CornflowerBlue));

        using RenderNodeRasterization unfilteredRasterization = Rasterize(unfiltered);
        using RenderNodeRasterization stockRasterization = Rasterize(stock);
        using RenderNodeRasterization restatedRasterization = Rasterize(restated);

        Assert.Multiple(() =>
        {
            Assert.That(stockRasterization.Bounds, Is.EqualTo(unfilteredRasterization.Bounds),
                "extracting the seams must not move an unmodified node's output bounds");
            Assert.That(restatedRasterization.Bounds, Is.EqualTo(unfilteredRasterization.Bounds));
            Assert.That(unfilteredRasterization.Bitmap, Is.Not.Null);
            Assert.That(stockRasterization.Bitmap, Is.Not.Null);
            Assert.That(restatedRasterization.Bitmap, Is.Not.Null);
            AssertBitmapsEqual(unfilteredRasterization.Bitmap!, stockRasterization.Bitmap!);
            AssertBitmapsEqual(stockRasterization.Bitmap!, restatedRasterization.Bitmap!);
        });
    }

    private static FilterEffect.Resource CreateIdentityEffectResource()
        => new IdentityEffectItemEffect().ToResource(CompositionContext.Default);

    private static RenderNode CreateEffectNode(FilterEffectRenderNode filter, RenderNode child)
    {
        filter.AddChild(child);
        return new OwnedEffectNode(filter);
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode node)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    OutputScale = 2,
                    TargetDomain = new Rect(0, 0, 64, 64),
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = new CpuTargetFactory(),
            });

    private static RenderNodeMeasurement Measure(RenderNode node)
    {
        using RenderNodeRenderer renderer = CreateRenderer(node);
        return renderer.Measure();
    }

    private static RenderNodeRasterization Rasterize(RenderNode node)
    {
        using RenderNodeRenderer renderer = CreateRenderer(node);
        return renderer.Rasterize();
    }

    private static void AssertBitmapsEqual(Bitmap expected, Bitmap actual)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.Width, Is.EqualTo(expected.Width));
            Assert.That(actual.Height, Is.EqualTo(expected.Height));
            Assert.That(actual.ColorType, Is.EqualTo(expected.ColorType));
            Assert.That(actual.AlphaType, Is.EqualTo(expected.AlphaType));
        });

        ReadOnlySpan<byte> expectedPixels = expected.GetPixelSpan();
        ReadOnlySpan<byte> actualPixels = actual.GetPixelSpan();
        float maximumChannelError = 0;
        for (int offset = 0; offset < expectedPixels.Length; offset += sizeof(ushort))
        {
            float expectedValue = (float)BitConverter.UInt16BitsToHalf(
                BinaryPrimitives.ReadUInt16LittleEndian(expectedPixels[offset..]));
            float actualValue = (float)BitConverter.UInt16BitsToHalf(
                BinaryPrimitives.ReadUInt16LittleEndian(actualPixels[offset..]));
            maximumChannelError = MathF.Max(maximumChannelError, MathF.Abs(expectedValue - actualValue));
        }

        Assert.That(
            maximumChannelError,
            Is.LessThanOrEqualTo(0.0025f),
            "Identity Skia filters may round RGBA16F channels while crossing effectItem buffers, "
            + "but must remain within a strict sub-visual-error bound.");
    }

    private sealed class ScopePublishingFilterNode(FilterEffect.Resource resource)
        : FilterEffectRenderNode(resource)
    {
        public int PublishCalls { get; private set; }

        public int LastLoweredCount { get; private set; }

        public Rect LastLoweredBounds { get; private set; }

        protected override void PublishLoweredResult(
            RenderNodeContext context,
            IReadOnlyList<RenderFragmentHandle> lowered)
        {
            PublishCalls++;
            LastLoweredCount = lowered.Count;

            Rect scope = default;
            foreach (RenderFragmentHandle fragment in lowered)
                scope = scope.Union(context.GetRecordedMetadataHint(fragment).Bounds);

            LastLoweredBounds = scope;
            context.Publish(context.TargetLayerScope(lowered, TargetRegion.Region(scope)));
        }
    }

    private sealed class AlwaysIsolatingFilterNode(FilterEffect.Resource resource)
        : FilterEffectRenderNode(resource)
    {
        public int IsolationQueries { get; private set; }

        public int IsolateCalls { get; private set; }

        public bool LastDomainWasFinite { get; private set; }

        protected override bool RequiresInputIsolation(RenderFragmentHandle input)
        {
            IsolationQueries++;
            return true;
        }

        protected override RenderFragmentHandle IsolateInputs(
            RenderNodeContext context,
            IReadOnlyList<RenderFragmentHandle> inputs,
            Rect isolationDomain)
        {
            IsolateCalls++;
            LastDomainWasFinite = !isolationDomain.IsInvalid;
            return base.IsolateInputs(context, inputs, isolationDomain);
        }
    }

    /// <remarks>
    /// Restates the documented default of all three seams using only public API. If a seam's default ever needed
    /// something a plugin author cannot reach, this node could not be written.
    /// </remarks>
    private sealed class ExplicitBaseBehaviourFilterNode(FilterEffect.Resource resource)
        : FilterEffectRenderNode(resource)
    {
        protected override bool RequiresInputIsolation(RenderFragmentHandle input)
            => !input.CanBeUsedAsValueInput;

        protected override RenderFragmentHandle IsolateInputs(
            RenderNodeContext context,
            IReadOnlyList<RenderFragmentHandle> inputs,
            Rect isolationDomain)
            => isolationDomain.IsInvalid
                ? context.OwningTargetLayer(inputs)
                : context.Layer(inputs, isolationDomain);

        protected override void PublishLoweredResult(
            RenderNodeContext context,
            IReadOnlyList<RenderFragmentHandle> lowered)
            => context.PublishRange(lowered);
    }

    /// <remarks>Mirrors what DrawableGroup's content isolation node does, using only public API.</remarks>
    private sealed class PluginContentIsolationNode : ContainerRenderNode
    {
        public bool ObservedSymbolicTargetWrite { get; private set; }

        public Rect ObservedInputBounds { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            ObservedSymbolicTargetWrite = context.HasSymbolicInputTargetWrite();
            ObservedInputBounds = context.CalculateRecordedInputBoundsHint();
            TargetRegion region = ObservedSymbolicTargetWrite
                ? TargetRegion.Full
                : TargetRegion.Region(ObservedInputBounds);
            context.Publish(context.TargetLayerScope(context.Inputs, region));
        }
    }

    /// <remarks>
    /// Makes the isolate-vs-defer choice a container render node has to make, the way FilterEffectRenderNode
    /// makes it, using only public API.
    /// </remarks>
    private sealed class PluginIsolatingContainerNode : ContainerRenderNode
    {
        public bool ResolvedFiniteDomain { get; private set; }

        public Rect ObservedDomain { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            ResolvedFiniteDomain = context.TryCalculateFiniteIsolationDomain(out Rect domain);
            ObservedDomain = domain;
            context.Publish(ResolvedFiniteDomain && domain.Width > 0 && domain.Height > 0
                ? context.Layer(context.Inputs, domain)
                : context.OwningTargetLayer(context.Inputs));
        }
    }

    private sealed class ExecutionCounter
    {
        public int Count { get; set; }
    }

    private sealed class RawTargetWriteNode(ExecutionCounter counter) : RenderNode
    {
        private static readonly RawTargetCommandDefinition<ExecutionCounter> s_definition =
            RawTargetCommandDefinition<ExecutionCounter>.Create(
                static (_, state) => state.Count++,
                Rect.Empty,
                RenderHitTestContract.None);

        public override void Process(RenderNodeContext context)
            => context.Publish(context.RawTargetCommand(s_definition.Call(counter)));
    }

    [SuppressResourceClassGeneration]
    private sealed partial class IdentityEffectItemEffect : FilterEffect
    {
        public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
        {
            context.Blur(Size.Empty);
            context.Transform(Matrix.Identity, BitmapInterpolationMode.Default);
        }

        public override Resource ToResource(CompositionContext context)
        {
            var resource = new Resource();
            bool updateOnly = false;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }

        public new sealed class Resource : FilterEffect.Resource
        {
            public Resource()
            {
            }
        }
    }

    private sealed class SolidSourceNode(Rect bounds, Color color) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            OpaqueRenderCall<(Rect bounds, Color color)> call = RenderDefinitionCallFactory.Opaque(
                (bounds, color),
                static (session, state) =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(canvas => canvas.Clear(state.color));
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Source(bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale);
            context.Publish(context.OpaqueSource(call));
        }
    }

    private sealed class OwnedEffectNode(FilterEffectRenderNode node) : RenderNode
    {
        public override void Process(RenderNodeContext context)
            => context.PublishRange(context.RecordSubtree(node));

        protected override void OnDispose(bool disposing)
        {
            if (disposing)
                node.Dispose();

            base.OnDispose(disposing);
        }
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
