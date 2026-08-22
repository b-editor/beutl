using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics3DTests;

// A renderer with no TargetFactory is reusable: it can render into a caller-owned destination and
// then rasterize on its own. The second request carries no context of its own, so the pool has to
// keep allocating on the context the first one bound — otherwise the target it creates fails the
// compatibility check the pool runs on every surface it hands out. Only a real shared GPU context
// separates the two allocation paths, which is why these are Vulkan-gated.
[TestFixture]
public sealed class RenderTargetPoolRetainedContextTests
{
    [Test]
    public void ATargetLessRequestAfterACpuDestinationStaysOnTheCpu()
    {
        GpuTestEnvironment.EnsureAvailable();

        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var pool = new RenderTargetPool(factory: null);
            using RenderTarget destination = CreateCpuTarget(new PixelSize(8, 8));

            using (RenderTargetPoolRequest request = pool.BeginRequest(destination))
            {
                using PooledRenderTargetLease lease = request.Acquire(new PixelSize(4, 4));
                Assert.That(lease.Target.Value.Context, Is.Null, "the destination is a CPU surface");
            }

            using (RenderTargetPoolRequest request = pool.BeginRequest())
            {
                Assert.That(request.ExpectedContextHandle, Is.Null, "a target-less request names no context");

                PooledRenderTargetLease? lease = null;
                Assert.That(
                    () => lease = request.Acquire(new PixelSize(6, 6)),
                    Throws.Nothing,
                    "the pool must not hand back a target from a context it will then reject");
                using (lease)
                {
                    Assert.That(
                        lease!.Target.Value.Context,
                        Is.Null,
                        "allocating on the shared GPU backend here is what makes the pool reject its own target");
                }
            }
        });
    }

    [Test]
    public void ATargetLessRequestAfterASharedContextDestinationStaysOnThatContext()
    {
        GpuTestEnvironment.EnsureAvailable();

        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var pool = new RenderTargetPool(factory: null);
            using RenderTarget? destination = RenderTarget.Create(8, 8);
            Assert.That(destination, Is.Not.Null);
            nint? destinationContext = destination!.Value.Context?.Handle;

            using (RenderTargetPoolRequest request = pool.BeginRequest(destination))
            {
                using PooledRenderTargetLease lease = request.Acquire(new PixelSize(4, 4));
                Assert.That(lease.Target.Value.Context?.Handle, Is.EqualTo(destinationContext));
            }

            using (RenderTargetPoolRequest request = pool.BeginRequest())
            {
                using PooledRenderTargetLease lease = request.Acquire(new PixelSize(6, 6));
                Assert.That(lease.Target.Value.Context?.Handle, Is.EqualTo(destinationContext));
            }
        });
    }

    private static RenderTarget CreateCpuTarget(PixelSize size)
    {
        SKSurface surface = SKSurface.Create(new SKImageInfo(
            size.Width,
            size.Height,
            SKColorType.RgbaF16,
            SKAlphaType.Premul,
            SKColorSpace.CreateSrgbLinear()))!;
        return new TestCpuRenderTarget(surface, size);
    }

    private sealed class TestCpuRenderTarget(SKSurface surface, PixelSize size)
        : RenderTarget(surface, size.Width, size.Height);
}
