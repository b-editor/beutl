using Beutl.Graphics;
using Beutl.Graphics.Rendering;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class OrphanedTargetEffectContractTests
{
    private static readonly Rect s_bounds = new(0, 0, 4, 3);

    public enum TargetEffectKind
    {
        TargetCommand,
        TargetScope,
        TargetLayerScope,
    }

    [TestCase(TargetEffectKind.TargetCommand)]
    [TestCase(TargetEffectKind.TargetScope)]
    [TestCase(TargetEffectKind.TargetLayerScope)]
    public void UnpublishedTargetEffect_FailsTheRecordingInsteadOfSilentlyDoingNothing(
        TargetEffectKind kind)
    {
        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(ExecutingSource("orphan-source"));
            _ = RecordTargetEffect(context, kind, source);
            context.Publish(source);
        });

        Assert.That(
            () => Rasterize(node),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.StartsWith(
                    "A recorded target-effect fragment was neither published nor consumed. "
                    + "Publish it, wrap it in a fragment you publish, or call Drop to abandon it "
                    + "deliberately.")
                .And.Message.Contains($"Fragment kind: {kind}"));
    }

    [TestCase(TargetEffectKind.TargetCommand)]
    [TestCase(TargetEffectKind.TargetScope)]
    [TestCase(TargetEffectKind.TargetLayerScope)]
    public void TargetEffectConsumedByAPublishedFragment_Executes(TargetEffectKind kind)
    {
        int executions = 0;
        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(ExecutingSource("consumed-source"));
            RenderFragmentHandle effect = RecordTargetEffect(
                context,
                kind,
                source,
                () => executions++);
            context.Publish(context.Layer([effect], s_bounds));
        });

        using RenderNodeRasterization rasterization = Rasterize(node);

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(executions, Is.EqualTo(1));
        });
    }

    [Test]
    public void AbandonedBlendAndOpacityWrappers_StayLegal()
    {
        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(ExecutingSource("wrapper-source"));
            _ = context.Blend(source, Beutl.Graphics.BlendMode.Multiply);
            _ = context.Opacity(source, 0.5f);
            context.Publish(source);
        });

        using RenderNodeRasterization rasterization = Rasterize(node);

        Assert.That(rasterization.IsEmpty, Is.False);
    }

    [Test]
    public void Drop_AbandonsARecordedTargetEffectWithoutExecutingIt()
    {
        int executions = 0;
        bool metadataIsConcrete = false;
        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(ExecutingSource("dropped-source"));
            RenderFragmentHandle command = RecordTargetEffect(
                context,
                TargetEffectKind.TargetCommand,
                source,
                () => executions++);
            metadataIsConcrete = command.TryGetMetadata(out _);
            context.Drop(command);
            context.Publish(source);
        });

        using RenderNodeRasterization rasterization = Rasterize(node);

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(metadataIsConcrete, Is.True);
            Assert.That(executions, Is.Zero);
        });
    }

    [Test]
    public void Drop_SurvivesAbsorptionIntoTheRecordingParent()
    {
        using var inner = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(ExecutingSource("nested-source"));
            context.Drop(RecordTargetEffect(context, TargetEffectKind.TargetCommand, source));
            context.Publish(source);
        });
        using var outer = new DelegateNode(context =>
        {
            context.PublishRange(context.RecordNode(inner, []));
        });

        using RenderNodeRasterization rasterization = Rasterize(outer);

        Assert.That(rasterization.IsEmpty, Is.False);
    }

    [TestCase(TargetEffectKind.TargetCommand)]
    [TestCase(TargetEffectKind.TargetScope)]
    [TestCase(TargetEffectKind.TargetLayerScope)]
    public void UnpublishedTargetEffectInAChildNode_FailsThatChildsOwnRecording(TargetEffectKind kind)
    {
        using var inner = new OrphaningChildNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(ExecutingSource("nested-orphan-source"));
            _ = RecordTargetEffect(context, kind, source);
            context.Publish(source);
        });
        using var outer = new DelegateNode(context =>
        {
            context.PublishRange(context.RecordNode(inner, []));
        });

        Assert.That(
            () => Rasterize(outer),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.StartsWith(
                    "A recorded target-effect fragment was neither published nor consumed.")
                .And.Message.Contains($"Fragment kind: {kind}")
                .And.Message.Contains($"recorded by: {typeof(OrphaningChildNode).FullName}"));
    }

    // Drop is not transitive and a parent never receives handles to a child's internal fragments.
    [Test]
    public void AChildTargetEffectPublicationTheParentNeverConsumes_StaysLegal()
    {
        int executions = 0;
        using var inner = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(ExecutingSource("nested-abandoned-source"));
            RenderFragmentHandle command = RecordTargetEffect(
                context,
                TargetEffectKind.TargetCommand,
                source,
                () => executions++);
            context.PublishRange([source, command]);
        });
        using var outer = new DelegateNode(context =>
        {
            IReadOnlyList<RenderFragmentHandle> outputs = context.RecordNode(inner, []);
            context.Publish(outputs[0]);
        });

        using RenderNodeRasterization rasterization = Rasterize(outer);

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(executions, Is.Zero);
        });
    }

    [Test]
    public void AnEntirelyAbandonedChildRecording_StaysLegalAndDrawsNothing()
    {
        using var inner = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(ExecutingSource("discarded-source"));
            context.Publish(RecordTargetEffect(context, TargetEffectKind.TargetScope, source));
        });
        using var outer = new DelegateNode(context =>
        {
            _ = context.RecordNode(inner, []);
        });

        using RenderNodeRasterization rasterization = Rasterize(outer);

        Assert.That(rasterization.IsEmpty, Is.True);
    }

    [Test]
    public void AChildDroppingAnInputItWasHanded_AbandonsTheParentsOwnTargetEffect()
    {
        using var inner = new DelegateNode(context => context.Drop(context.Inputs[0]));
        using var outer = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(ExecutingSource("handed-over-source"));
            RenderFragmentHandle command = RecordTargetEffect(
                context,
                TargetEffectKind.TargetCommand,
                source);
            context.RecordNode(inner, [command]);
            context.Publish(context.OpaqueSource(ExecutingSource("surviving-source")));
        });

        using RenderNodeRasterization rasterization = Rasterize(outer);

        Assert.That(rasterization.IsEmpty, Is.False);
    }

    [Test]
    public void Drop_RejectsAHandleFromAnotherTransaction()
    {
        RenderFragmentHandle? foreign = null;
        using var inner = new DelegateNode(context => context.Drop(foreign!));
        using var outer = new DelegateNode(context =>
        {
            foreign = context.OpaqueSource(ExecutingSource("foreign-source"));
            context.RecordNode(inner, []);
            context.Publish(foreign);
        });

        Assert.That(
            () => Rasterize(outer),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.EqualTo(
                    "The render fragment handle belongs to a different recording transaction."));
    }

    [Test]
    public void Drop_RejectsAnAlreadyPublishedHandle()
    {
        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(ExecutingSource("published-source"));
            context.Publish(source);
            context.Drop(source);
        });

        Assert.That(
            () => Rasterize(node),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.EqualTo(
                    "The render fragment was already published and cannot be dropped."));
    }

    [Test]
    public void Publish_RejectsAnAlreadyDroppedHandle()
    {
        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(ExecutingSource("redeemed-source"));
            context.Drop(source);
            context.Publish(source);
        });

        Assert.That(
            () => Rasterize(node),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.EqualTo(
                    "The render fragment was already dropped and cannot be published."));
    }

    private static RenderFragmentHandle RecordTargetEffect(
        RenderNodeContext context,
        TargetEffectKind kind,
        RenderFragmentHandle source,
        Action? onExecute = null)
    {
        return kind switch
        {
            TargetEffectKind.TargetCommand => context.TargetCommand(
                [source],
                RenderDefinitionCallFactory.TargetCommand(
                    _ => onExecute?.Invoke(),
                    TargetRegion.Region(s_bounds),
                    s_bounds,
                    RenderHitTestContract.None)),
            TargetEffectKind.TargetScope => context.TargetScope(
                source,
                RenderDefinitionCallFactory.TargetScope(
                    session =>
                    {
                        onExecute?.Invoke();
                        session.Canvas.Use(_ => session.ReplayInput());
                    },
                    RenderBoundsContract.Identity,
                    RenderHitTestContract.AnyInput,
                    RenderScaleContract.PreserveInputSupply)),
            TargetEffectKind.TargetLayerScope => context.TargetLayerScope(
                [
                    onExecute is null
                        ? context.ContributeValues(source)
                        : RecordTargetEffect(context, TargetEffectKind.TargetCommand, source, onExecute)
                ],
                TargetRegion.Region(s_bounds)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static OpaqueRenderCall<Action<OpaqueRenderSession>> ExecutingSource(object _)
    {
        return RenderDefinitionCallFactory.Opaque(
            static session =>
            {
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                session.Publish(output);
            },
            OpaqueRenderBoundsContract.Source(s_bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale);
    }

    private static RenderNodeRasterization Rasterize(RenderNode node)
    {
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_bounds,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });
        return renderer.Rasterize();
    }

    private sealed class DelegateNode(Action<RenderNodeContext> process) : RenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }

    private sealed class OrphaningChildNode(Action<RenderNodeContext> process) : RenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }
}
