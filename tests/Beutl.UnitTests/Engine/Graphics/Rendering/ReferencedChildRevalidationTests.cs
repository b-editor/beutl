using System.Collections.Immutable;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.Threading;
using static Beutl.UnitTests.Engine.Graphics.Rendering.RendererTestSupport;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[NonParallelizable]
[TestFixture]
public class ReferencedChildRevalidationTests
{
    [Test]
    public void ReferencedChild_ReportsAChangeOnlyWhenTheReferencePointsSomewhereElse()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            using var first = new RectangleRenderNode(new Rect(0, 0, 4, 4), Brushes.Resource.White, null);
            using var second = new RectangleRenderNode(new Rect(0, 0, 4, 4), Brushes.Resource.White, null);
            var drawable = new ReferencingDrawable(first);
            using Renderer renderer = CreateCpuRenderer(16, 16);

            renderer.Render(CreateFrame(new(16, 16), drawable));
            bool steadyState = drawable.LastRecordingObservedChange;

            drawable.Child = second;
            renderer.Render(CreateFrame(new(16, 16), drawable));
            bool afterSwap = drawable.LastRecordingObservedChange;

            Assert.Multiple(() =>
            {
                Assert.That(steadyState, Is.False,
                    "Re-recording an unchanged reference must not invalidate what depends on it.");
                Assert.That(afterSwap, Is.True,
                    "Pointing the reference at another node must revalidate it.");
                Assert.That(FindReference(renderer, drawable).Child, Is.SameAs(second));
            });
        });
    }

    [Test]
    public void ARecordingFailure_LeavesTheRendererAbleToRecordTheNextFrame()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            var drawable = new SwitchableFaultingDrawable();
            using Renderer renderer = CreateCpuRenderer(16, 16);
            renderer.Render(CreateFrame(new(16, 16), drawable));

            drawable.ShouldFault = true;
            Assert.Throws<InvalidOperationException>(() => renderer.Render(CreateFrame(new(16, 16), drawable)));

            drawable.ShouldFault = false;
            Assert.DoesNotThrow(
                () => renderer.Render(CreateFrame(new(16, 16), drawable)),
                "A failed recording must not poison the retained node tree.");
        });
    }

    private static ReferencesChildRenderNode FindReference(Renderer renderer, Drawable drawable)
    {
        DrawableRenderNode node = renderer.FindRenderNode(drawable)
            ?? throw new InvalidOperationException("The drawable was never recorded.");
        return node.Children.OfType<ReferencesChildRenderNode>().Single();
    }
}

// Top-level partial because EngineObjectResourceGenerator does not support nested types.
internal sealed partial class ReferencingDrawable : Drawable
{
    public ReferencingDrawable(RenderNode child)
    {
        Child = child;
    }

    public RenderNode Child { get; set; }

    public bool LastRecordingObservedChange { get; private set; }

    private void RenderContent(GraphicsContext2D context)
        => context.DrawNode(
            Child,
            static node => new ReferencesChildRenderNode(node),
            (reference, node) => LastRecordingObservedChange = reference.Update(node));

    public new partial class Resource
    {
        protected override void RenderCore(GraphicsContext2D context) => RequireOriginal().RenderContent(context);

        protected override Size MeasureCore(Size availableSize) => new(4, 4);

        protected override void OnDraw(GraphicsContext2D context)
        {
        }
    }
}

internal sealed partial class SwitchableFaultingDrawable : Drawable
{
    public bool ShouldFault { get; set; }

    public int RenderCalls { get; private set; }

    private void RenderContent(GraphicsContext2D context)
    {
        RenderCalls++;
        context.DrawRectangle(new Rect(0, 0, 4, 4), Brushes.Resource.White, null);
        if (ShouldFault)
        {
            throw new InvalidOperationException("recording failed");
        }
    }

    public new partial class Resource
    {
        protected override void RenderCore(GraphicsContext2D context) => RequireOriginal().RenderContent(context);

        protected override Size MeasureCore(Size availableSize) => new(4, 4);

        protected override void OnDraw(GraphicsContext2D context)
        {
        }
    }
}
