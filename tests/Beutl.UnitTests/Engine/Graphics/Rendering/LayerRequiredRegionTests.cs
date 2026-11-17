using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// A layer spans all of its content, which can reach far past the frame: particles travelling off it, a shockwave
// scaled far up, a picture zoomed into. Only the part of it the frame needs has to be drawn.
[TestFixture]
public sealed class LayerRequiredRegionTests
{
    private static readonly PixelSize s_frame = new(200, 120);

    // Far enough that drawing the layer whole needs a buffer wider than the bound asserted below.
    private const float FarOffset = 4000;

    [Test]
    public void AFiniteLayerFarLargerThanTheFrame_AllocatesOnlyWhatTheFrameNeeds()
    {
        static RenderNode Create(bool far)
        {
            FilterEffectRenderNode blur = CreateBlurNode(sigma: 3);
            var layer = new LayerRenderNode(new Rect(-40, -40, (far ? FarOffset + 100 : 0) + 300, 200));
            AddContent(layer, far);
            blur.AddChild(layer);
            return blur;
        }

        AssertFarContentCostsNothing(Create);
    }

    [Test]
    public void AContentIsolationFarLargerThanTheFrame_AllocatesOnlyWhatTheFrameNeeds()
    {
        static RenderNode Create(bool far)
        {
            // A scaled group isolates its content in a target layer scope that spans all of that content.
            var transform = new TransformRenderNode(
                Matrix.CreateTranslation(-100, -60)
                * Matrix.CreateScale(1.015f, 1.015f)
                * Matrix.CreateTranslation(100, 60),
                TransformOperator.Prepend);
            var isolation = new ContentIsolationRenderNode(isolatesContent: true);
            AddContent(isolation, far);
            transform.AddChild(isolation);
            return transform;
        }

        AssertFarContentCostsNothing(Create);
    }

    private static void AssertFarContentCostsNothing(Func<bool, RenderNode> create)
    {
        var nearSizes = new List<PixelSize>();
        var farSizes = new List<PixelSize>();
        using Bitmap near = Render(create(false), nearSizes);
        using Bitmap far = Render(create(true), farSizes);

        Assert.Multiple(() =>
        {
            Assert.That(farSizes, Is.Not.Empty, "the fixture must materialize the layer");
            Assert.That(far.GetPixelSpan().ToArray(), Is.EqualTo(near.GetPixelSpan().ToArray()),
                "content outside the frame must not change what the frame shows");
            Assert.That(farSizes.Select(static size => Math.Max(size.Width, size.Height)),
                Is.All.LessThanOrEqualTo(512),
                "no intermediate may reach the far content's extent");
        });
    }

    private static void AddContent(ContainerRenderNode layer, bool far)
    {
        layer.AddChild(new RectangleRenderNode(new Rect(20, 20, 60, 40), Brushes.Resource.White, null));
        // Content just past the frame keeps the layer's own edge out of sight in both renders, so the frame compares
        // what the far content adds rather than how a buffer's edge is sampled.
        layer.AddChild(new RectangleRenderNode(new Rect(230, 90, 20, 20), Brushes.Resource.White, null));
        var rotated = new TransformRenderNode(Matrix.CreateRotation(MathF.PI / 7) * Matrix.CreateTranslation(130, 30),
            TransformOperator.Prepend);
        rotated.AddChild(new RectangleRenderNode(new Rect(0, 0, 50, 30), Brushes.Resource.White, null));
        layer.AddChild(rotated);
        if (far)
            layer.AddChild(new RectangleRenderNode(new Rect(FarOffset, 30, 50, 50), Brushes.Resource.White, null));
    }

    private static Bitmap Render(RenderNode root, List<PixelSize> requestedSizes)
    {
        using RenderNode ownedRoot = root;
        using var destination = new CpuRenderTarget(s_frame);
        using var canvas = new ImmediateCanvas(destination, RenderIntent.Delivery, logicalSize: s_frame.ToSize(1));
        using var renderer = new RenderNodeRenderer(ownedRoot, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Delivery,
            TargetDomain = new Rect(default, s_frame.ToSize(1)),
            OutputScale = 1,
            MaxWorkingScale = 1,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            Purpose = RenderRequestPurpose.Frame,
        }, new RecordingCpuTargetFactory(requestedSizes));

        renderer.Render(canvas);
        return destination.Snapshot();
    }

    private static FilterEffectRenderNode CreateBlurNode(float sigma)
    {
        var blur = new Blur();
        blur.Sigma.Value = new Size(sigma, sigma);
        return new FilterEffectRenderNode(blur.ToResource(CompositionContext.Default));
    }

    private sealed class RecordingCpuTargetFactory(List<PixelSize> requestedSizes) : IRenderTargetFactory
    {
        public RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            requestedSizes.Add(allocation.DeviceSize);
            return new CpuRenderTarget(allocation.DeviceSize);
        }
    }
}
