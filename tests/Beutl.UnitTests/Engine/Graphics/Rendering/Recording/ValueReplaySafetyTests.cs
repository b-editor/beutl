using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using static Beutl.UnitTests.Engine.Graphics.Rendering.RenderGraphTestSupport;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[TestFixture]
public sealed class ValueReplaySafetyTests
{
    /// <remarks>
    /// Every composition is resolved into an input-space matrix before the scope is read, so all three publish a value replay map.
    /// </remarks>
    [TestCase(TransformOperator.Prepend)]
    [TestCase(TransformOperator.Append)]
    [TestCase(TransformOperator.Set)]
    public void Transform_PublishesEveryCompositionAsAValueReplayMap(TransformOperator transformOperator)
    {
        using var transform = new TransformRenderNode(
            Matrix.CreateTranslation(3.25f, 4.5f),
            transformOperator);
        transform.AddChild(new RectangleRenderNode(
            new Rect(2, 3, 12, 8),
            Brushes.Resource.White,
            null));
        using var request = CreateRequest(cacheEnabled: false);

        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(transform);
        RenderFragmentReference root = GetSingleRoot(graph);
        var payload = (TargetScopeRenderFragmentPayload)root.Payload!;

        Assert.Multiple(() =>
        {
            Assert.That(payload.Description.IsValueReplayMap, Is.True);
            Assert.That(root.CanBeUsedAsValueInput, Is.True);
        });
    }

    [Test]
    public void ValueReplayMap_AllowsSelfContainedFiniteLayerCacheCandidate()
    {
        using var node = new FiniteLayerReplayNode();
        node.Cache.RecordStableRequests();
        using var request = CreateRequest(cacheEnabled: true, RenderRequestPurpose.Frame);

        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        RenderFragmentReference root = GetSingleRoot(graph);
        var cacheContext = new RenderCacheResolutionContext(
            RenderCacheFormatIdentity.LinearPremultipliedRgba16Float,
            new RenderCacheDeviceContextIdentity("device", "context"));
        using CompiledRenderRequest compiled = new RenderRequestCompiler(
            renderCacheContext: cacheContext).Compile(request, graph);
        RenderCacheDecision decision = compiled.CacheResolution.Decisions.Single();

        Assert.Multiple(() =>
        {
            Assert.That(root.HasTargetEffects, Is.True,
                "A finite Layer still owns target-scoped execution metadata.");
            Assert.That(RenderFragmentTargetDependency.HasExternalTargetDependency(root), Is.False);
            Assert.That(root.CanBeUsedAsValueInput, Is.True);
            Assert.That(graph.CacheCandidates, Has.Length.EqualTo(1));
            Assert.That(decision.Kind, Is.EqualTo(RenderCacheResolutionKind.MissCapture));
        });
    }

    /// <remarks>
    /// An ambient composition is resolved into the input's own space before planning reads it, so the density
    /// it carries back to its input is the one the step between them needs - the same answer the identical
    /// matrix gets as a Prepend.
    /// </remarks>
    [TestCase(TransformOperator.Prepend)]
    [TestCase(TransformOperator.Append)]
    public void ATransformedLayerMaterializesAtTheDensityItsScopeEnlargesItBy(
        TransformOperator transformOperator)
    {
        var factory = new FailureTestTargetFactory();
        using RenderNode root = CreateTransformedLayer(
            Matrix.CreateScale(4, 4),
            transformOperator,
            new Rect(0, 0, 8, 6));
        using var renderer = CreateRenderer(root, factory);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(
                factory.Targets.Select(static target => new PixelSize(target.Width, target.Height)),
                Does.Contain(new PixelSize(32, 24)),
                "The layer must be rasterized at the density its scope draws it at.");
        });
    }

    [Test]
    public void PerspectiveTransform_LayerRendersWithoutScalarDensity()
    {
        var perspective = new Rotation3DTransform
        {
            RotationY = { CurrentValue = 55 },
            Depth = { CurrentValue = 400 },
        };
        using RenderNode root = CreateTransformedLayer(
            perspective.CreateMatrix(Composition.CompositionContext.Default),
            TransformOperator.Prepend,
            new Rect(0, 0, 24, 16));
        using var renderer = CreateRenderer(root, new FailureTestTargetFactory());

        Assert.That(() => renderer.Rasterize().Dispose(), Throws.Nothing);
    }

    private static RenderRequest CreateRequest(
        bool cacheEnabled,
        RenderRequestPurpose purpose = RenderRequestPurpose.Auxiliary)
        => new(new RenderRequestOptions(
            RenderIntent.Preview,
            purpose,
            targetDomain: new Rect(0, 0, 64, 64),
            outputScale: 1,
            maxWorkingScale: 1,
            cachePolicy: cacheEnabled ? RenderCacheOptions.Enabled : RenderCacheOptions.Disabled));

    private static RenderNodeRenderer CreateRenderer(
        RenderNode root,
        IRenderTargetFactory targetFactory)
        => new(root, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            OutputScale = 1,
            MaxWorkingScale = 8,
            CacheOptions = RenderCacheOptions.Disabled,
        }, targetFactory);

    private static RenderNode CreateTransformedLayer(
        Matrix transform,
        TransformOperator transformOperator,
        Rect bounds)
    {
        var root = new TransformRenderNode(transform, transformOperator);
        var layer = new TestLayerRenderNode(bounds);
        layer.AddChild(new RectangleRenderNode(bounds, Brushes.Resource.White, null));
        root.AddChild(layer);
        return root;
    }

    private static TargetScopeDescription CreateIdentityValueReplayDescription(string key)
        => TargetScopeDescription.CreateValueReplayMap(
            key,
            static (session, replayKey) => session.Canvas.Use(_ => session.ReplayInput()),
            RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            RenderScaleContract.PreserveInputSupply,
            deviceGridSensitivity: RenderDeviceGridSensitivity.Insensitive,
            deviceGridMapping: RenderDeviceGridMapping.Preserved);

    private sealed class FiniteLayerReplayNode : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            var bounds = new Rect(0, 0, 24, 16);
            RenderFragmentHandle source = context.OpaqueSource(OpaqueRenderDescription.CreateRequestLocal(
                session =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(bounds);
                    output.Canvas.Use(canvas => canvas.Clear(Colors.White));
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Source(bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.Vector));
            RenderFragmentHandle layer = context.Layer([source], bounds);
            context.Publish(context.TargetScope(
                layer,
                CreateIdentityValueReplayDescription(nameof(FiniteLayerReplayNode))));
        }
    }
}
