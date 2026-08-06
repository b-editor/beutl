using System.Collections.Immutable;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[NonParallelizable]
[TestFixture]
public class ReferencedChildRevalidationTests
{
    [Test]
    public void Frame_ClearsMarksOnANodeBehindAReference()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            using var child = new ContainerRenderNode();
            var grandchild = new RectangleRenderNode(new Rect(0, 0, 4, 4), Brushes.Resource.White, null);
            child.AddChild(grandchild);
            using Renderer renderer = CreateRenderer();
            CompositionFrame frame = CreateFrame(new ReferencingDrawable(child));

            renderer.UpdateFrame(frame);
            child.HasChanges = true;
            grandchild.HasChanges = true;
            renderer.UpdateFrame(frame);

            Assert.Multiple(() =>
            {
                Assert.That(child.HasChanges, Is.False, "the referenced child kept its mark");
                Assert.That(grandchild.HasChanges, Is.False, "a node under the referenced child kept its mark");
            });
        });
    }

    [Test]
    public void StableFrames_CountAChildReachedByTwoReferencesOncePerFrame()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            using var child = new ContainerRenderNode();
            using Renderer renderer = CreateRenderer();
            CompositionFrame frame = CreateFrame(
                new ReferencingDrawable(child),
                new ReferencingDrawable(child));

            child.Cache.ReportRenderCount(RenderNodeCache.Count - 2);
            renderer.UpdateFrame(frame);
            bool afterOneFrame = child.Cache.CanCache();
            renderer.UpdateFrame(frame);

            Assert.Multiple(() =>
            {
                Assert.That(afterOneFrame, Is.False, "the shared child advanced twice in one frame");
                Assert.That(child.Cache.CanCache(), Is.True, "the referenced child's render count never advanced");
            });
        });
    }

    [Test]
    public void Frame_SkipsADisposedReferencedChild()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            var disposedChild = new ContainerRenderNode();
            disposedChild.Cache.ReportRenderCount(RenderNodeCache.Count);
            disposedChild.HasChanges = true;
            disposedChild.Dispose();

            using var liveChild = new ContainerRenderNode();
            liveChild.HasChanges = true;
            using Renderer renderer = CreateRenderer();
            CompositionFrame frame = CreateFrame(
                new ReferencingDrawable(disposedChild),
                new ReferencingDrawable(liveChild));

            renderer.UpdateFrame(frame);

            Assert.Multiple(() =>
            {
                Assert.That(disposedChild.Cache.CanCache(), Is.True, "a disposed referenced child was revalidated");
                Assert.That(liveChild.HasChanges, Is.False, "the live referenced child kept its mark");
            });
        });
    }

    private static Renderer CreateRenderer()
        => new(
            width: 16,
            height: 16,
            renderScale: 1,
            maxWorkingScale: float.PositiveInfinity,
            diagnostics: null,
            surface: new CpuRenderTarget(16, 16));

    private static CompositionFrame CreateFrame(params Drawable[] drawables)
        => new(
            [.. drawables.Select(static drawable =>
                (EngineObject.Resource)drawable.ToResource(CompositionContext.Default))],
            new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            new PixelSize(16, 16));

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            width,
            height);
}

// Top-level partial because EngineObjectResourceGenerator does not support nested types.
internal sealed partial class ReferencingDrawable(RenderNode child) : Drawable
{
    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
        => context.DrawNode(
            child,
            static node => new ReferencesChildRenderNode(node),
            static (reference, node) => reference.Update(node));

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => new(4, 4);

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
}
