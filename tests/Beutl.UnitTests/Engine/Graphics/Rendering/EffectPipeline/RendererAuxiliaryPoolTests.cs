using System.Collections.Immutable;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

[NonParallelizable]
[TestFixture]
public class RendererAuxiliaryPoolTests
{
    [Test]
    public void AuxiliaryFrameUpdate_DoesNotMutateFrameRenderNodeCacheState()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var shape = new RectShape
            {
                Width = { CurrentValue = 64 },
                Height = { CurrentValue = 48 },
                Fill = { CurrentValue = Brushes.White },
            };
            using Drawable.Resource frameResource = shape.ToResource(new CompositionContext(
                TimeSpan.Zero, RenderIntent.Delivery, RenderPullPurpose.Frame));
            using Drawable.Resource auxiliaryResource = shape.ToResource(new CompositionContext(
                TimeSpan.Zero, RenderIntent.Delivery, RenderPullPurpose.Auxiliary));
            var frame = new CompositionFrame(
                [frameResource], default, new PixelSize(128, 96),
                RenderIntent.Delivery, RenderPullPurpose.Frame);
            var auxiliary = new CompositionFrame(
                [auxiliaryResource], default, new PixelSize(128, 96),
                RenderIntent.Delivery, RenderPullPurpose.Auxiliary);
            using var renderer = new Renderer(128, 96, RenderIntent.Delivery);

            renderer.UpdateFrame(frame);
            DrawableRenderNode frameNode = renderer.FindRenderNode(shape)!;
            frameNode.Cache.ReportRenderCount(RenderNodeCache.Count);
            Assert.That(frameNode.Cache.CanCache(), Is.True, "sanity: frame warm-up state is ready");

            renderer.UpdateFrame(auxiliary);
            DrawableRenderNode auxiliaryNode = renderer.FindRenderNode(shape)!;

            Assert.Multiple(() =>
            {
                Assert.That(auxiliaryNode, Is.Not.SameAs(frameNode),
                    "auxiliary resources require a separate retained node tree");
                Assert.That(frameNode.Cache.CanCache(), Is.True,
                    "auxiliary node updates must not reset frame cache warm-up state");
            });

            renderer.UpdateFrame(frame);
            Assert.That(renderer.FindRenderNode(shape), Is.SameAs(frameNode));
        });
    }

    [Test]
    public void FindRenderNode_ReturnsNestedNodeFromCurrentPullPurpose()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var child = new RectShape
            {
                Width = { CurrentValue = 16 },
                Height = { CurrentValue = 16 },
                Fill = { CurrentValue = Brushes.White },
            };
            var decorator = new DrawableDecorator();
            using Drawable.Resource frameChild = child.ToResource(new CompositionContext(
                TimeSpan.Zero, RenderIntent.Delivery, RenderPullPurpose.Frame));
            using Drawable.Resource auxiliaryChild = child.ToResource(new CompositionContext(
                TimeSpan.Zero, RenderIntent.Delivery, RenderPullPurpose.Auxiliary));
            var frameContext = new CompositionContext(
                TimeSpan.Zero, RenderIntent.Delivery, RenderPullPurpose.Frame)
            {
                Flow = new List<EngineObject.Resource> { frameChild },
            };
            var auxiliaryContext = new CompositionContext(
                TimeSpan.Zero, RenderIntent.Delivery, RenderPullPurpose.Auxiliary)
            {
                Flow = new List<EngineObject.Resource> { auxiliaryChild },
            };
            using Drawable.Resource frameDecorator = decorator.ToResource(frameContext);
            using Drawable.Resource auxiliaryDecorator = decorator.ToResource(auxiliaryContext);
            var frame = new CompositionFrame(
                [frameDecorator], default, new PixelSize(32, 32),
                RenderIntent.Delivery, RenderPullPurpose.Frame);
            var auxiliary = new CompositionFrame(
                [auxiliaryDecorator], default, new PixelSize(32, 32),
                RenderIntent.Delivery, RenderPullPurpose.Auxiliary);
            using var renderer = new Renderer(32, 32, RenderIntent.Delivery);

            renderer.UpdateFrame(frame);
            DrawableRenderNode? frameNode = renderer.FindRenderNode(child);
            renderer.UpdateFrame(auxiliary);
            DrawableRenderNode? auxiliaryNode = renderer.FindRenderNode(child);

            Assert.Multiple(() =>
            {
                Assert.That(frameNode, Is.Not.Null);
                Assert.That(auxiliaryNode, Is.Not.Null);
                Assert.That(auxiliaryNode, Is.Not.SameAs(frameNode));
                Assert.That(auxiliaryNode!.Drawable?.Resource, Is.SameAs(auxiliaryChild));
            });
        });
    }

    [Test]
    public void FindRenderNode_DoesNotReturnCachedNodeOmittedFromCurrentFrame()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var first = new RectShape
            {
                Width = { CurrentValue = 16 },
                Height = { CurrentValue = 16 },
                Fill = { CurrentValue = Brushes.White },
            };
            var second = new RectShape
            {
                Width = { CurrentValue = 8 },
                Height = { CurrentValue = 8 },
                Fill = { CurrentValue = Brushes.White },
            };
            using Drawable.Resource firstResource = first.ToResource(new CompositionContext(
                TimeSpan.Zero, RenderIntent.Delivery, RenderPullPurpose.Frame));
            using Drawable.Resource secondResource = second.ToResource(new CompositionContext(
                TimeSpan.Zero, RenderIntent.Delivery, RenderPullPurpose.Frame));
            var firstFrame = new CompositionFrame(
                [firstResource], default, new PixelSize(32, 32),
                RenderIntent.Delivery, RenderPullPurpose.Frame);
            var secondFrame = new CompositionFrame(
                [secondResource], default, new PixelSize(32, 32),
                RenderIntent.Delivery, RenderPullPurpose.Frame);
            using var renderer = new Renderer(32, 32, RenderIntent.Delivery);

            renderer.UpdateFrame(firstFrame);
            DrawableRenderNode? firstNode = renderer.FindRenderNode(first);
            renderer.UpdateFrame(secondFrame);

            Assert.Multiple(() =>
            {
                Assert.That(firstNode, Is.Not.Null, "sanity: the first frame created a retained node");
                Assert.That(renderer.FindRenderNode(first), Is.Null,
                    "retention in the same-purpose cache must not make an omitted drawable current");
                Assert.That(renderer.FindRenderNode(second), Is.Not.Null);
            });
        });
    }

    [Test]
    public void Renderer_RejectsCompositionFramesWithTheWrongPurposeProvenance()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var frame = new CompositionFrame(
                ImmutableArray<EngineObject.Resource>.Empty,
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(128, 96),
                RenderIntent.Delivery,
                RenderPullPurpose.Frame);
            var auxiliary = new CompositionFrame(
                ImmutableArray<EngineObject.Resource>.Empty,
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(128, 96),
                RenderIntent.Delivery,
                RenderPullPurpose.Auxiliary);
            var previewFrame = new CompositionFrame(
                ImmutableArray<EngineObject.Resource>.Empty,
                frame.Time,
                frame.Size,
                RenderIntent.Preview,
                RenderPullPurpose.Frame);
            var previewAuxiliary = new CompositionFrame(
                ImmutableArray<EngineObject.Resource>.Empty,
                frame.Time,
                frame.Size,
                RenderIntent.Preview,
                RenderPullPurpose.Auxiliary);
            using var renderer = new Renderer(128, 96, RenderIntent.Delivery);

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentException>(() => renderer.Render(auxiliary));
                Assert.Throws<ArgumentException>(() => renderer.Render(previewFrame));
                Assert.Throws<ArgumentException>(() => renderer.HitTest(frame, new Point(64, 48)));
                Assert.Throws<ArgumentException>(() => renderer.HitTest(previewAuxiliary, new Point(64, 48)));
                Assert.Throws<ArgumentException>(() => renderer.GetBoundaries(frame, 0));
                Assert.Throws<ArgumentException>(() => renderer.GetBoundary(frame, new RectShape()));
                Assert.Throws<ArgumentException>(() => renderer.RecalculateBoundaries(frame, 0));
            });
        });
    }

    [Test]
    public void RecalculateBoundaries_AfterFrameRender_ReusesTheRenderedBounds()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            PoolProbeRenderNode.ProcessCount = 0;
            var shape = new RectShape
            {
                Width = { CurrentValue = 64 },
                Height = { CurrentValue = 48 },
                Fill = { CurrentValue = Brushes.White },
                FilterEffect = { CurrentValue = new PoolProbeEffect() },
            };
            Drawable.Resource resource = shape.ToResource(new CompositionContext(
                TimeSpan.Zero,
                RenderIntent.Delivery,
                RenderPullPurpose.Frame));
            var frame = new CompositionFrame(
                ImmutableArray.Create<EngineObject.Resource>(resource),
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(128, 96),
                RenderIntent.Delivery,
                RenderPullPurpose.Frame);

            using var renderer = new Renderer(128, 96, RenderIntent.Delivery);
            renderer.Render(frame);
            int frameProcessCount = PoolProbeRenderNode.ProcessCount;

            Rect[] boundaries = renderer.RecalculateBoundaries(shape.ZIndex);

            Assert.Multiple(() =>
            {
                Assert.That(boundaries, Has.Length.EqualTo(1));
                Assert.That(boundaries[0], Is.EqualTo(new Rect(32, 24, 64, 48)));
                Assert.That(PoolProbeRenderNode.ProcessCount, Is.EqualTo(frameProcessCount),
                    "The selection overlay must reuse bounds collected during the frame pull instead of executing the effect pipeline again.");
            });
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AuxiliaryPull_ForwardsRendererPoolToFilterEffects(bool hitTest)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            PoolProbeRenderNode.SawPool = false;
            PoolProbeRenderNode.SawAuxiliaryPull = false;
            var shape = new RectShape
            {
                Width = { CurrentValue = 64 },
                Height = { CurrentValue = 48 },
                Fill = { CurrentValue = Brushes.White },
                FilterEffect = { CurrentValue = new PoolProbeEffect() },
            };
            Drawable.Resource resource = shape.ToResource(new CompositionContext(
                TimeSpan.Zero,
                RenderIntent.Delivery,
                RenderPullPurpose.Auxiliary));
            var frame = new CompositionFrame(
                ImmutableArray.Create<EngineObject.Resource>(resource),
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(128, 96),
                RenderIntent.Delivery,
                RenderPullPurpose.Auxiliary);

            using var renderer = new Renderer(128, 96, RenderIntent.Delivery);
            renderer.Diagnostics.Reset();
            if (hitTest)
                renderer.HitTest(frame, new Point(64, 48));
            else
            {
                renderer.UpdateFrame(frame);
                renderer.RecalculateBoundaries(shape.ZIndex);
            }

            Assert.Multiple(() =>
            {
                Assert.That(PoolProbeRenderNode.SawPool, Is.True,
                    "HitTest/RecalculateBoundaries must preserve the renderer pool on the auxiliary pull");
                Assert.That(PoolProbeRenderNode.SawAuxiliaryPull, Is.True,
                    "HitTest/RecalculateBoundaries must mark the pull as cache-state-neutral auxiliary work");
                Assert.That(renderer.Diagnostics.Snapshot().GpuPasses, Is.Zero,
                    "auxiliary pulls must not contaminate frame-rendering diagnostics");
            });
        });
    }
}

[SuppressResourceClassGeneration]
internal sealed partial class PoolProbeEffect : CustomRenderNodeFilterEffect
{
    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = false;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : CustomRenderNodeFilterEffect.Resource
    {
        private static readonly FilterEffectRenderNodeFactory s_factory =
            FilterEffectRenderNodeFactory.Of<Resource, PoolProbeRenderNode>(static r => new PoolProbeRenderNode(r));

        public override FilterEffectRenderNodeFactory RenderNodeFactory
            => s_factory;
    }
}

internal sealed class PoolProbeRenderNode(FilterEffect.Resource resource) : FilterEffectRenderNode(resource)
{
    internal static bool SawPool { get; set; }

    internal static bool SawAuxiliaryPull { get; set; }

    internal static int ProcessCount { get; set; }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        ProcessCount++;
        SawPool |= context.Pool != null;
        SawAuxiliaryPull |= context.IsAuxiliaryPull;
        if (context.Diagnostics != null)
            context.Diagnostics.GpuPasses++;
        return context.Input;
    }
}
