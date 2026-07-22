using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class GeometryAuthoringContractTests
{
    [Test]
    public void Geometry_ExposesZeroOrOneBoundsHitTestResourceAndEligibilityContracts()
    {
        var inputBounds = new Rect(10, 20, 8, 6);
        var outputBounds = inputBounds.Inflate(new Thickness(2));
        var resource = new GeometryResource("metadata");
        GeometryDescription? observedDescription = null;
        RenderResourceIdentity observedResourceIdentity = default;
        FragmentSnapshot observedFragment = default;
        int renderCalls = 0;

        using var node = new DelegateNode(context =>
        {
            RenderResource<GeometryResource> token = context.Borrow(resource, "metadata-resource", version: 4);
            observedResourceIdentity = token.CacheIdentity;
            GeometryDescription description = GeometryDescription.Create(
                _ => renderCalls++,
                RenderBoundsContract.Create(
                    bounds => bounds.Inflate(new Thickness(2)),
                    required => required.Inflate(new Thickness(2)),
                    structuralKey: "geometry-inflate-two"),
                RenderHitTestContract.Custom(
                    GeometryHitTest,
                    structuralKey: "geometry-output-hit"),
                structuralKey: "public-geometry",
                runtimeIdentity: new RenderRuntimeIdentity("geometry-runtime"),
                requiresReadback: true,
                resources: [token]);
            RenderFragmentHandle source = context.OpaqueSource(MetadataSource(inputBounds));
            RenderFragmentHandle geometry = context.Geometry(source, description);
            RenderFragmentHandle command = context.TargetCommand(
                [],
                TargetCommandDescription.Create(
                    static _ => throw new AssertionException("Metadata must not execute target commands."),
                    TargetRegion.Region(inputBounds),
                    Rect.Empty,
                    RenderHitTestContract.None,
                    TargetAccess.ReadWrite,
                    structuralKey: "geometry-ineligible-command"));

            Assert.That(() => context.Geometry(command, description), Throws.TypeOf<ArgumentException>());
            observedDescription = description;
            observedFragment = FragmentSnapshot.From(geometry);
            context.Publish(geometry);
        });

        using var renderer = CreateRenderer(node, outputScale: 2, maxWorkingScale: 3);
        RenderNodeMeasurement measurement = renderer.Measure();
        bool hit = renderer.HitTest(new Point(outputBounds.X + 1, outputBounds.Y + 1));

        Assert.Multiple(() =>
        {
            Assert.That(renderCalls, Is.Zero, "Bounds and hit-test requests are metadata-only.");
            Assert.That(hit, Is.True);
            Assert.That(measurement.OutputBounds, Is.EqualTo(outputBounds));
            Assert.That(measurement.QueryBounds, Is.EqualTo(outputBounds));
            Assert.That(observedFragment.Bounds, Is.EqualTo(outputBounds));
            Assert.That(observedFragment.Cardinality, Is.EqualTo(RenderValueCardinality.ZeroOrOne));
            Assert.That(observedFragment.EffectiveScale, Is.EqualTo(EffectiveScale.At(2)));
            Assert.That(observedFragment.ContributesValues, Is.True);
            Assert.That(observedFragment.CanBeUsedAsValueInput, Is.True);

            Assert.That(observedDescription, Is.Not.Null);
            Assert.That(observedDescription!.Bounds.TransformBounds(inputBounds), Is.EqualTo(outputBounds));
            Assert.That(observedDescription.Bounds.GetRequiredInputBounds(outputBounds),
                Is.EqualTo(outputBounds.Inflate(new Thickness(2))));
            Assert.That(observedDescription.StructuralKey, Is.EqualTo("public-geometry"));
            Assert.That(observedDescription.RuntimeIdentity,
                Is.EqualTo(new RenderRuntimeIdentity("geometry-runtime")));
            Assert.That(observedDescription.RequiresReadback, Is.True);
            Assert.That(observedDescription.Resources, Has.Count.EqualTo(1));
            Assert.That(observedResourceIdentity,
                Is.EqualTo(new RenderResourceIdentity("metadata-resource", 4)));
            Assert.That(
                () => _ = observedDescription.Resources[0].CacheIdentity,
                Throws.TypeOf<ObjectDisposedException>());
            Assert.That(resource.DisposeCalls, Is.Zero);
        });
    }

    [Test]
    public void FilterEffectGeometry_UsesDeclaredReadbackAndResourceShrinksOutputAndRejectsRetainedFacades()
    {
        var bounds = new Rect(0, 0, 8, 6);
        var shrink = new Rect(2, 1, 3, 3);
        var declared = new GeometryResource("declared");
        var effect = new PluginGeometryEffect(declared, shrink);
        using PluginGeometryEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using FilterEffectRenderNode node = resource.CreateRenderNode();
        node.AddChild(new SolidSourceNode(bounds, Colors.White));

        using var renderer = CreateRenderer(node, outputScale: 1, maxWorkingScale: 2);
        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(effect.ExecutionCalls, Is.EqualTo(1));
            Assert.That(effect.ResourceUses, Is.EqualTo(1));
            Assert.That(effect.SnapshotUses, Is.EqualTo(1));
            Assert.That(effect.ObservedResource, Is.SameAs(declared));
            Assert.That(effect.ObservedInputRasterBounds,
                Is.EqualTo(PixelRect.FromRect(bounds, 1).ToRect(1)));
            Assert.That(effect.ObservedCanvasRasterBounds,
                Is.EqualTo(PixelRect.FromRect(bounds, 1).ToRect(1)));
            Assert.That(rasterization.Bounds, Is.EqualTo(bounds),
                "Runtime shrink does not narrow recording-time conservative bounds.");
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(AlphaAt(rasterization.Bitmap!, 3, 2), Is.GreaterThan(0.9f));
            Assert.That(AlphaAt(rasterization.Bitmap!, 0, 0), Is.LessThan(0.01f));
            Assert.That(AlphaAt(rasterization.Bitmap!, 7, 5), Is.LessThan(0.01f));
            Assert.That(declared.DisposeCalls, Is.Zero, "Borrowed Geometry resources remain externally owned.");

            Assert.That(() => _ = effect.RetainedSession!.OutputBounds,
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => _ = effect.RetainedInput!.Bounds,
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => _ = effect.RetainedCanvas!.Density,
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => effect.RetainedCanvas!.Use(static _ => { }),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => effect.RetainedInput!.UseSnapshot(static _ => { }),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => effect.RetainedSnapshot!.GetPixelSpan(),
                Throws.TypeOf<ObjectDisposedException>());
        });
    }

    [Test]
    public void Geometry_DiscardOutputWinsOverShrinkAndLeavesAConservativeTransparentResult()
    {
        var bounds = new Rect(0, 0, 6, 4);
        int executionCalls = 0;
        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(ExecutingSource(bounds, Colors.White));
            GeometryDescription description = GeometryDescription.Create(
                session =>
                {
                    executionCalls++;
                    session.Canvas.Use(canvas => session.Input.Draw(canvas));
                    session.SetOutputBounds(new Rect(1, 1, 2, 2));
                    session.DiscardOutput();
                },
                RenderBoundsContract.Identity,
                RenderHitTestContract.OutputBounds,
                structuralKey: "discard-after-shrink",
                runtimeIdentity: new RenderRuntimeIdentity("discard-after-shrink-runtime"));
            RenderFragmentHandle geometry = context.Geometry(source, description);
            Assert.That(geometry.ValueCardinality, Is.EqualTo(RenderValueCardinality.ZeroOrOne));
            context.Publish(geometry);
        });

        using RenderNodeRasterization rasterization = Rasterize(node);

        Assert.Multiple(() =>
        {
            Assert.That(executionCalls, Is.EqualTo(1));
            Assert.That(rasterization.Bounds, Is.EqualTo(bounds));
            Assert.That(rasterization.IsEmpty, Is.False,
                "A non-empty conservative request returns an owned bitmap even when Geometry discards its value.");
            Assert.That(MaxAlpha(rasterization.Bitmap!), Is.LessThan(0.01f));
        });
    }

    [Test]
    public void Geometry_InputSnapshotIsUnavailableUnlessReadbackWasDeclared()
    {
        var bounds = new Rect(0, 0, 4, 3);
        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(ExecutingSource(bounds, Colors.White));
            GeometryDescription description = GeometryDescription.Create(
                session => session.Input.UseSnapshot(static _ => { }),
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput,
                structuralKey: "undeclared-readback",
                requiresReadback: false);
            context.Publish(context.Geometry(source, description));
        });

        Assert.That(
            () =>
            {
                using RenderNodeRasterization _ = Rasterize(node);
            },
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("readback was not declared"));
    }

    private static RenderNodeRenderer CreateRenderer(
        RenderNode node,
        float outputScale,
        float maxWorkingScale)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                OutputScale = outputScale,
                MaxWorkingScale = maxWorkingScale,
                UseRenderCache = false,
                TargetFactory = new CpuTargetFactory(),
            });

    private static RenderNodeRasterization Rasterize(RenderNode node)
    {
        using var renderer = CreateRenderer(node, outputScale: 1, maxWorkingScale: 2);
        return renderer.Rasterize();
    }

    private static float AlphaAt(Bitmap bitmap, int x, int y)
    {
        Assert.That(bitmap.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
        return (float)bitmap.GetRow<Half>(y)[x * 4 + 3];
    }

    private static float MaxAlpha(Bitmap bitmap)
    {
        float max = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            Span<Half> row = bitmap.GetRow<Half>(y);
            for (int x = 0; x < bitmap.Width; x++)
                max = Math.Max(max, (float)row[x * 4 + 3]);
        }

        return max;
    }

    private static OpaqueRenderDescription MetadataSource(Rect bounds)
    {
        return OpaqueRenderDescription.Create(
            static _ => throw new AssertionException("A metadata request must not execute the source."),
            RenderOperationBoundsContract.Source(bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.Vector,
            structuralKey: (typeof(GeometryAuthoringContractTests), "metadata-source", bounds));
    }

    private static bool GeometryHitTest(RenderHitTestContext context, Point point)
    {
        Assert.Multiple(() =>
        {
            Assert.That(context.OutputBounds, Is.EqualTo(new Rect(8, 18, 12, 10)));
            Assert.That(context.Inputs, Has.Count.EqualTo(1));
            Assert.That(context.Inputs[0].Bounds, Is.EqualTo(new Rect(10, 20, 8, 6)));
        });
        return context.OutputBounds.Contains(point);
    }

    private static OpaqueRenderDescription ExecutingSource(Rect bounds, Color color)
    {
        return OpaqueRenderDescription.Create(
            session =>
            {
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                output.Canvas.Use(canvas => canvas.Clear(color));
                session.Publish(output);
            },
            RenderOperationBoundsContract.Source(bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale,
            structuralKey: (typeof(GeometryAuthoringContractTests), "executing-source"),
            runtimeIdentity: new RenderRuntimeIdentity((bounds, color)));
    }

    private sealed class SolidSourceNode(Rect bounds, Color color) : RenderNode
    {
        public override void Process(RenderNodeContext context)
            => context.Publish(context.OpaqueSource(ExecutingSource(bounds, color)));
    }

    private sealed class DelegateNode(Action<RenderNodeContext> process) : RenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }

    [SuppressResourceClassGeneration]
    private sealed partial class PluginGeometryEffect(
        GeometryResource declaredResource,
        Rect shrinkBounds) : FilterEffect
    {
        public int ExecutionCalls { get; private set; }

        public int ResourceUses { get; private set; }

        public int SnapshotUses { get; private set; }

        public GeometryResource? ObservedResource { get; private set; }

        public GeometrySession? RetainedSession { get; private set; }

        public RenderExecutionInput? RetainedInput { get; private set; }

        public RenderCallbackCanvas? RetainedCanvas { get; private set; }

        public Bitmap? RetainedSnapshot { get; private set; }

        public Rect ObservedInputRasterBounds { get; private set; }

        public Rect ObservedCanvasRasterBounds { get; private set; }

        public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
        {
            RenderResource<GeometryResource> token = context.Borrow(
                declaredResource,
                cacheKey: "plugin-geometry-resource",
                version: 2);
            context.Geometry(GeometryDescription.Create(
                session =>
                {
                    ExecutionCalls++;
                    RetainedSession = session;
                    RetainedInput = session.Input;
                    RetainedCanvas = session.Canvas;
                    ObservedInputRasterBounds = session.Input.RasterBounds;
                    ObservedCanvasRasterBounds = session.Canvas.RasterBounds;
                    session.UseResource(token, value =>
                    {
                        ResourceUses++;
                        ObservedResource = value;
                    });
                    session.Input.UseSnapshot(bitmap =>
                    {
                        SnapshotUses++;
                        RetainedSnapshot = bitmap;
                        Assert.That(bitmap.IsDisposed, Is.False);
                    });
                    Assert.That(
                        () => session.Input.UseSnapshot(static _ => { }),
                        Throws.TypeOf<InvalidOperationException>(),
                        "A declared snapshot is still a one-shot lease.");
                    Assert.That(
                        () => session.SetOutputBounds(new Rect(-1, -1, 20, 20)),
                        Throws.TypeOf<ArgumentException>(),
                        "Geometry may shrink but cannot grow beyond its allocated forward bounds.");
                    session.Canvas.Use(canvas => session.Input.Draw(canvas));
                    session.SetOutputBounds(shrinkBounds);
                },
                RenderBoundsContract.Identity,
                RenderHitTestContract.OutputBounds,
                structuralKey: "plugin-geometry",
                runtimeIdentity: new RenderRuntimeIdentity("plugin-geometry-runtime"),
                requiresReadback: true,
                resources: [token]));
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
            public override FilterEffectRenderNode CreateRenderNode() => new(this);
        }
    }

    private sealed class GeometryResource(string name) : IDisposable
    {
        public string Name { get; } = name;

        public int DisposeCalls { get; private set; }

        public void Dispose() => DisposeCalls++;
    }

    private readonly record struct FragmentSnapshot(
        Rect Bounds,
        EffectiveScale EffectiveScale,
        RenderValueCardinality Cardinality,
        bool ContributesValues,
        bool CanBeUsedAsValueInput)
    {
        public static FragmentSnapshot From(RenderFragmentHandle handle)
        {
            Assert.That(handle.TryGetMetadata(out RenderFragmentMetadata metadata), Is.True);
            return new FragmentSnapshot(
                metadata.Bounds,
                metadata.EffectiveScale,
                handle.ValueCardinality,
                handle.ContributesValuesToTarget,
                handle.CanBeUsedAsValueInput);
        }
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(PixelSize deviceSize) => new CpuRenderTarget(deviceSize);
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
