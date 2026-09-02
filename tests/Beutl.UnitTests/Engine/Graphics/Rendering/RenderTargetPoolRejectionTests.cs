using System.Collections.Concurrent;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public sealed class RenderTargetPoolRejectionTests
{
    [Test]
    public void ATargetSharingAnOwnedSurface_IsRefusedWithoutDestroyingIt()
    {
        var factory = new SurfaceSharingFactory();
        using var registry = new RenderTargetLeaseRegistry(factory);
        using RenderTargetLeaseSession session = registry.BeginSession(RenderIntent.Preview);

        RenderTargetLease first = session.Acquire(new PixelSize(8, 8));
        RenderTarget owned = first.Target;

        // The first lease is still held, so a second request of the same size cannot reuse that slot and has
        // to create; the factory answers it with another instance over the surface the pool already holds.
        Assert.That(
            () => session.Acquire(new PixelSize(8, 8)),
            Throws.InstanceOf<InvalidOperationException>()
                .With.Message.Contains("already in use"));

        Assert.Multiple(() =>
        {
            Assert.That(owned.IsDisposed, Is.False);
            Assert.That(
                () => owned.Value.Canvas.Clear(SKColors.Transparent),
                Throws.Nothing,
                "The refused instance must not have freed the surface the pool still owns.");
        });

        first.Dispose();
    }

    [Test]
    public void ARefusedTargetSharingAnOwnedSurface_IsNotLeftFinalizable()
    {
        var factory = new SurfaceSharingFactory();
        using var registry = new RenderTargetLeaseRegistry(factory);
        using RenderTargetLeaseSession session = registry.BeginSession(RenderIntent.Preview);

        RenderTargetLease first = session.Acquire(new PixelSize(8, 8));

        Assert.That(
            () => session.Acquire(new PixelSize(8, 8)),
            Throws.InstanceOf<InvalidOperationException>()
                .With.Message.Contains("already in use"));

        // Nothing references the refused instance any more, so a surviving finalizer registration is the only
        // thing that can still reach its surface release.
        CollectAndFinalize();

        Assert.That(
            factory.RefusedDisposals, Is.Empty,
            "A refused wrapper over a live surface must stop being finalizable: its Dispose(false) releases "
            + "the surface the pool's leased slot is still drawing to.");

        first.Dispose();
    }

    [Test]
    public void ARefusedCopyOfAnOwnedTarget_DropsItsSurfaceReference()
    {
        var factory = new SurfaceCopyingFactory();
        using var registry = new RenderTargetLeaseRegistry(factory);
        using RenderTargetLeaseSession session = registry.BeginSession(RenderIntent.Preview);

        RenderTargetLease first = session.Acquire(new PixelSize(8, 8));
        RenderTarget owned = first.Target;

        Assert.That(
            () => session.Acquire(new PixelSize(8, 8)),
            Throws.InstanceOf<InvalidOperationException>()
                .With.Message.Contains("already in use"));

        Assert.Multiple(() =>
        {
            Assert.That(owned.IsDisposed, Is.False);
            Assert.That(
                owned.SharesSurfaceOwnership, Is.False,
                "A refused reference-counted copy has to drop its count. Nothing else ever drops it, so the "
                + "surface would outlive every owner instead.");
        });

        first.Dispose();
    }

    private static void CollectAndFinalize()
    {
        for (int i = 0; i < 2; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    private static SKSurface CreateSurface(PixelSize size)
        => SKSurface.Create(new SKImageInfo(
               size.Width,
               size.Height,
               SKColorType.RgbaF16,
               SKAlphaType.Premul,
               SKColorSpace.CreateSrgbLinear()))
           ?? throw new InvalidOperationException("Could not create a CPU render target.");

    private sealed class SurfaceSharingFactory : IRenderTargetFactory
    {
        private SharedSurfaceRenderTarget? _first;

        /// <summary>Every <c>Dispose(bool)</c> the refused instance saw, with its <c>disposing</c> flag.</summary>
        public ConcurrentQueue<bool> RefusedDisposals { get; } = new();

        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
        {
            if (_first is null)
            {
                _first = new SharedSurfaceRenderTarget(
                    CreateSurface(allocation.DeviceSize),
                    allocation.DeviceSize,
                    ownsSurface: true);
                return _first;
            }

            // Deliberately wrong: a new instance over a surface the pool already owns. It is deliberately not
            // stored, so the pool's own handling is the only thing keeping it out of the finalizer queue.
            return new SharedSurfaceRenderTarget(
                _first.BackingSurface,
                allocation.DeviceSize,
                ownsSurface: false,
                RefusedDisposals);
        }
    }

    private sealed class SurfaceCopyingFactory : IRenderTargetFactory
    {
        private RenderTarget? _first;

        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
        {
            if (_first is null)
            {
                _first = new SharedSurfaceRenderTarget(
                    CreateSurface(allocation.DeviceSize),
                    allocation.DeviceSize,
                    ownsSurface: true);
                return _first;
            }

            // Deliberately wrong: a reference-counted copy of a target the pool already owns.
            return _first.ShallowCopy();
        }
    }

    private sealed class SharedSurfaceRenderTarget(
        SKSurface surface,
        PixelSize size,
        bool ownsSurface,
        ConcurrentQueue<bool>? disposals = null)
        : RenderTarget(surface, size.Width, size.Height)
    {
        public SKSurface BackingSurface { get; } = surface;

        protected override void Dispose(bool disposing)
        {
            disposals?.Enqueue(disposing);

            // Only the allocating instance may reach the base release. Letting a shared wrapper through would
            // free the surface under the pool's live slot and turn a red assertion into a native crash.
            if (ownsSurface)
                base.Dispose(disposing);
        }
    }
}
