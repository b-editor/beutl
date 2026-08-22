using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>
/// Pins that rejecting a factory's target never takes down a surface something else is still using.
/// </summary>
/// <remarks>
/// A factory can hand back a fresh target instance wrapping a surface the pool already owns. Refusing it is
/// right, but disposing it would free that surface underneath the live slot that holds it, and the next draw
/// into that slot writes to freed memory rather than failing.
/// </remarks>
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

    private sealed class SurfaceSharingFactory : IRenderTargetFactory
    {
        private SharedSurfaceRenderTarget? _first;

        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
        {
            if (_first is null)
            {
                _first = new SharedSurfaceRenderTarget(
                    CreateSurface(allocation.DeviceSize),
                    allocation.DeviceSize);
                return _first;
            }

            // Deliberately wrong: a new instance over a surface the pool already owns.
            return new SharedSurfaceRenderTarget(_first.BackingSurface, allocation.DeviceSize);
        }

        private static SKSurface CreateSurface(PixelSize size)
            => SKSurface.Create(new SKImageInfo(
                   size.Width,
                   size.Height,
                   SKColorType.RgbaF16,
                   SKAlphaType.Premul,
                   SKColorSpace.CreateSrgbLinear()))
               ?? throw new InvalidOperationException("Could not create a CPU render target.");
    }

    private sealed class SharedSurfaceRenderTarget(SKSurface surface, PixelSize size)
        : RenderTarget(surface, size.Width, size.Height)
    {
        public SKSurface BackingSurface { get; } = surface;
    }
}
