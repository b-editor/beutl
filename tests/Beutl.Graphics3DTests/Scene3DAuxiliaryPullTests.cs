using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics3D;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics3DTests;

/// <summary>
/// Auxiliary pulls of a 3D scene feed real pixels (brush previews, node-graph thumbnails, headless stills), so
/// <see cref="Scene3DRenderNode"/> must render the scene for them — the earlier empty-draw operation produced fully
/// transparent thumbnails/swatches. The auxiliary render must also leave the shared frame renderer untouched.
/// </summary>
[TestFixture]
[NonParallelizable]
public class Scene3DAuxiliaryPullTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        GpuTestEnvironment.EnsureAvailable();
    }

    [Test]
    public void AuxiliaryPull_RendersScenePixels_WithoutTouchingTheFrameRenderer()
    {
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            var scene = new Scene3D();
            scene.RenderWidth.CurrentValue = 64f;
            scene.RenderHeight.CurrentValue = 48f;
            scene.BackgroundColor.CurrentValue = Colors.White;

            var resource = (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
            Scene3DRenderNode.SetGraphicsContextProviderForTest(() => GpuTestEnvironment.SharedContext);
            try
            {
                using var node = new Scene3DRenderNode(resource);
                var context = new RenderNodeContext(
                    [], RenderIntent.Preview, pullPurpose: RenderPullPurpose.Auxiliary);

                RenderNodeOperation[] ops = node.Process(context);
                try
                {
                    Assert.That(ops, Has.Length.EqualTo(1), "the auxiliary pull yields one scene operation");

                    using Bitmap composed = Rasterize(ops[0], 64, 48);
                    long nonBlack = CountNonBlack(composed);
                    TestContext.WriteLine($"auxiliary non-black pixels = {nonBlack}");
                    Assert.Multiple(() =>
                    {
                        Assert.That(nonBlack, Is.GreaterThan(0),
                            "the auxiliary operation must carry the rendered scene, not an empty draw");
                        Assert.That(resource.Renderer, Is.Null,
                            "an auxiliary pull must not create or resize the shared frame renderer");
                    });
                }
                finally
                {
                    RenderNodeOperation.DisposeAll(ops);
                }
            }
            finally
            {
                Scene3DRenderNode.SetGraphicsContextProviderForTest(null);
            }
        });
    }

    [Test]
    public void AuxiliaryPull_FailurePreservesPrimaryAndSweepsCopySurfaceAndRenderer()
    {
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            var primaryFailure = new InvalidOperationException("auxiliary copy failure");
            var copyCleanupFailure = new InvalidOperationException("copy cleanup failure");
            var surfaceCleanupFailure = new InvalidOperationException("surface cleanup failure");
            var disposed = new List<string>();
            Scene3DRenderNode.SetGraphicsContextProviderForTest(() => GpuTestEnvironment.SharedContext);
            Scene3DRenderNode.SetAuxiliaryCleanupHooksForTest(
                () => throw primaryFailure,
                resource =>
                {
                    switch (resource)
                    {
                        case RenderTarget:
                            disposed.Add("copy");
                            resource.Dispose();
                            throw copyCleanupFailure;
                        case SKSurface:
                            disposed.Add("surface");
                            resource.Dispose();
                            throw surfaceCleanupFailure;
                        case Renderer3D:
                            disposed.Add("renderer");
                            resource.Dispose();
                            break;
                        default:
                            resource.Dispose();
                            break;
                    }
                });
            try
            {
                var scene = new Scene3D();
                scene.RenderWidth.CurrentValue = 32;
                scene.RenderHeight.CurrentValue = 32;
                using var resource = (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
                using var node = new Scene3DRenderNode(resource);
                var context = new RenderNodeContext(
                    [], RenderIntent.Delivery, outputScale: 1f,
                    pullPurpose: RenderPullPurpose.Auxiliary);

                InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
                    () => node.Process(context));

                Assert.Multiple(() =>
                {
                    Assert.That(actual, Is.SameAs(primaryFailure));
                    Assert.That(disposed, Is.EqualTo(new[] { "copy", "surface", "renderer" }));
                });
            }
            finally
            {
                Scene3DRenderNode.SetAuxiliaryCleanupHooksForTest(null, null);
                Scene3DRenderNode.SetGraphicsContextProviderForTest(null);
            }
        });
    }

    [Test]
    public void AuxiliaryPull_SuccessfulRenderThrowsFirstCleanupFailureAndReclaimsOperation()
    {
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            var surfaceCleanupFailure = new InvalidOperationException("surface cleanup failure");
            var disposed = new List<string>();
            Scene3DRenderNode.SetGraphicsContextProviderForTest(() => GpuTestEnvironment.SharedContext);
            Scene3DRenderNode.SetAuxiliaryCleanupHooksForTest(
                null,
                resource =>
                {
                    switch (resource)
                    {
                        case SKSurface:
                            disposed.Add("surface");
                            resource.Dispose();
                            throw surfaceCleanupFailure;
                        case Renderer3D:
                            disposed.Add("renderer");
                            resource.Dispose();
                            break;
                        case RenderNodeOperation:
                            disposed.Add("operation");
                            resource.Dispose();
                            break;
                        default:
                            resource.Dispose();
                            break;
                    }
                });
            try
            {
                var scene = new Scene3D();
                scene.RenderWidth.CurrentValue = 32;
                scene.RenderHeight.CurrentValue = 32;
                using var resource = (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
                using var node = new Scene3DRenderNode(resource);
                var context = new RenderNodeContext(
                    [], RenderIntent.Delivery, outputScale: 1f,
                    pullPurpose: RenderPullPurpose.Auxiliary);

                InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
                    () => node.Process(context));

                Assert.Multiple(() =>
                {
                    Assert.That(actual, Is.SameAs(surfaceCleanupFailure));
                    Assert.That(disposed, Is.EqualTo(new[] { "surface", "renderer", "operation" }));
                });
            }
            finally
            {
                Scene3DRenderNode.SetAuxiliaryCleanupHooksForTest(null, null);
                Scene3DRenderNode.SetGraphicsContextProviderForTest(null);
            }
        });
    }

    private static Bitmap Rasterize(RenderNodeOperation op, int width, int height)
    {
        using RenderTarget target = RenderTarget.Create(width, height)!;
        using (var canvas = new ImmediateCanvas(
            target, RenderIntent.Preview, 1f, logicalSize: new Size(width, height)))
        {
            canvas.Clear(Colors.Black);
            op.Render(canvas);
        }

        return target.Snapshot();
    }

    private static long CountNonBlack(Bitmap bitmap)
    {
        long count = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                SkiaSharp.SKColor c = bitmap.SKBitmap.GetPixel(x, y);
                if (c.Red > 8 || c.Green > 8 || c.Blue > 8)
                    count++;
            }
        }

        return count;
    }
}
