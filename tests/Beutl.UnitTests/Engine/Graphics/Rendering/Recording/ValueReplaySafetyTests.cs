using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[TestFixture]
public sealed class ValueReplaySafetyTests
{
    [TestCase(TransformOperator.Prepend, true)]
    [TestCase(TransformOperator.Append, false)]
    [TestCase(TransformOperator.Set, false)]
    public void Transform_OnlyPrependPublishesAValueReplayMap(
        TransformOperator transformOperator,
        bool expectedValueReplay)
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
            Assert.That(payload.Description.IsValueReplayMap, Is.EqualTo(expectedValueReplay));
            Assert.That(root.CanBeUsedAsValueInput, Is.EqualTo(expectedValueReplay));
        });
    }

    [Test]
    public void ValueReplayMap_RejectsContributingTargetCapture()
    {
        using var node = new TargetCaptureReplayNode();
        using var request = CreateRequest(cacheEnabled: false);

        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        RenderFragmentReference root = GetSingleRoot(graph);

        Assert.Multiple(() =>
        {
            Assert.That(root.ContributesValuesToTarget, Is.True);
            Assert.That(root.ValueCardinality, Is.EqualTo(RenderValueCardinality.Single));
            Assert.That(root.CanBeUsedAsValueInput, Is.False,
                "A target capture must not be replayed against a fresh transparent value target.");
            Assert.That(RenderFragmentTargetDependency.HasExternalTargetDependency(root), Is.True);
        });
    }

    [Test]
    public void ValueReplayMap_AllowsSelfContainedFiniteLayerCacheCandidate()
    {
        using var node = new FiniteLayerReplayNode();
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
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

    private static RenderRequest CreateRequest(
        bool cacheEnabled,
        RenderRequestPurpose purpose = RenderRequestPurpose.Auxiliary)
        => new(new RenderRequestOptions(
            RenderIntent.Preview,
            purpose,
            targetDomain: new Rect(0, 0, 64, 64),
            outputScale: 1,
            maxWorkingScale: 1,
            cachePolicy: cacheEnabled ? RenderCacheOptions.Default : RenderCacheOptions.Disabled));

    private static RenderFragmentReference GetSingleRoot(RecordedRenderGraph graph)
    {
        RenderFragmentId rootId = graph.PublicationRoots.Single();
        return (RenderFragmentReference)graph.Fragments
            .Single(fragment => fragment.Id == rootId)
            .Payload!;
    }

    private static TargetScopeDescription CreateIdentityValueReplayDescription(string key)
        => TargetScopeDescription.CreateValueReplayMap(
            session => session.Canvas.Use(_ => session.ReplayInput()),
            RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            RenderScaleContract.PreserveInputSupply,
            key);

    private sealed class TargetCaptureReplayNode : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            var bounds = new Rect(0, 0, 24, 16);
            RenderFragmentHandle capture = context.TargetCapture(TargetCaptureDescription.Create(
                TargetRegion.Region(bounds),
                bounds,
                RenderHitTestContract.OutputBounds,
                RenderScaleContract.MaterializeAtWorkingScale));
            RenderFragmentHandle contributing = context.ContributeValues(capture);
            context.Publish(context.TargetScope(
                contributing,
                CreateIdentityValueReplayDescription(nameof(TargetCaptureReplayNode))));
        }
    }

    private sealed class FiniteLayerReplayNode : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            var bounds = new Rect(0, 0, 24, 16);
            RenderFragmentHandle source = context.OpaqueSource(OpaqueRenderDescription.Create(
                session =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(bounds);
                    output.Canvas.Use(canvas => canvas.Clear(Colors.White));
                    session.Publish(output);
                },
                RenderOperationBoundsContract.Source(bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.Vector,
                structuralKey: nameof(FiniteLayerReplayNode)));
            RenderFragmentHandle layer = context.Layer([source], bounds);
            context.Publish(context.TargetScope(
                layer,
                CreateIdentityValueReplayDescription(nameof(FiniteLayerReplayNode))));
        }
    }
}
