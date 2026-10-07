using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

public sealed partial class FilterEffectExecutor
{
    private void EnsureLinearTargets()
    {
        for (int index = 0; index < CurrentTargets.Count; index++)
        {
            EffectTarget source = CurrentTargets[index];
            if (RenderScaleUtilities.IsEmptyBounds(source.Bounds)
                || !RenderScaleUtilities.IsAllocatableBounds(source.Bounds))
                continue;
            if (source.RenderTarget is not { } renderTarget || renderTarget.ColorSpace == BitmapColorSpace.LinearSrgb)
                continue;

            EffectTarget? replacement;
            if (_renderTargetLeaseSession is { } session)
            {
                RenderTargetLease? lease = session.TryAcquire(source.DeviceBounds.Size,
                    pixelFormat: RenderTargetPixelFormat.LinearPremultipliedRgba16Float);
                if (lease is null)
                {
                    DropUnflushableTarget(source, index--, "Could not allocate the linear filter-effect input.");
                    continue;
                }

                try
                {
                    replacement = source.CreateReplacement(lease);
                }
                catch
                {
                    lease.Dispose();
                    throw;
                }
            }
            else
            {
                using RenderTarget? target = RenderTarget.Create(renderTarget.Width, renderTarget.Height,
                    RenderTargetPixelFormat.LinearPremultipliedRgba16Float);
                if (target is null)
                {
                    DropUnflushableTarget(source, index--, "Could not allocate the linear filter-effect input.");
                    continue;
                }

                replacement = source.CreateReplacement(target);
            }

            try
            {
                using var canvas = CreateExecutionCanvas(replacement.RenderTarget!, 1f,
                    new Size(renderTarget.Width, renderTarget.Height));
                using (canvas.PushDeviceSpace())
                    canvas.DrawRenderTargetPixelsWithoutFlush(renderTarget, 0, 0);
            }
            catch
            {
                replacement.Dispose();
                throw;
            }

            if (_pendingSkiaTargets?.Remove(source, out PendingSkiaTarget? pending) == true)
                _pendingSkiaTargets[replacement] = pending;
            CurrentTargets[index] = replacement;
            source.Dispose();
        }
    }
}
