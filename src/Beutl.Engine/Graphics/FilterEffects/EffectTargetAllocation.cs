using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

internal static class EffectTargetAllocation
{
    /// <summary>
    /// Allocates one effect-stage target, through the caller's lease session when there is one, and reports
    /// a declined allocation as <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// A configured <see cref="IRenderTargetFactory"/> is reachable only through the session, and its targets
    /// may come from a context the global allocator knows nothing about. Going around it here would both
    /// ignore the caller's allocation policy and mix surfaces from two contexts inside one stage.
    /// </remarks>
    public static EffectTarget? Allocate(
        RenderTargetLeaseSession? leaseSession,
        Rect bounds,
        float density,
        PixelRect deviceBounds,
        Vector deviceGridOffset,
        bool preserveImperativeRasterPlacement = false)
    {
        if (leaseSession is { HasTargetFactory: true })
        {
            RenderTargetLease? lease = leaseSession.TryAcquire(deviceBounds.Size);
            if (lease is null)
                return null;

            try
            {
                return EffectTarget.FromLease(
                    lease,
                    bounds,
                    EffectiveScale.At(density),
                    deviceBounds,
                    deviceGridOffset,
                    preserveImperativeRasterPlacement);
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        using RenderTarget? renderTarget = RenderTarget.Create(deviceBounds.Width, deviceBounds.Height);
        return renderTarget is null
            ? null
            : new EffectTarget(
                renderTarget,
                bounds,
                EffectiveScale.At(density),
                deviceBounds,
                deviceGridOffset,
                preserveImperativeRasterPlacement);
    }
}
