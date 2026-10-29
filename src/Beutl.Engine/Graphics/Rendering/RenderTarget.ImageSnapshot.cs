using SkiaSharp;

namespace Beutl.Graphics.Rendering;

public partial class RenderTarget
{
    /// <summary>
    /// Acquires an immutable Skia image for sampling on this target's rendering thread and context,
    /// without reading its pixels back to the CPU. The caller must dispose the lease on that thread.
    /// </summary>
    public RenderTargetImageLease AcquireImage()
    {
        VerifyAccess();
        PrepareForSampling(RenderTargetSamplingIntent.SameContextTextureSampling(RawValue.Context));
        return new RenderTargetImageLease(this);
    }
}

/// <summary>Keeps both a target and its image alive until all dependent draws have been recorded.</summary>
public sealed class RenderTargetImageLease : IDisposable
{
    private RenderTarget? _target;
    private readonly SurfaceSnapshot.Lease _snapshot;

    internal RenderTargetImageLease(RenderTarget target)
    {
        _target = target.ShallowCopy();
        try
        {
            _snapshot = SurfaceSnapshot.Take(target.RawValue);
        }
        catch
        {
            _target.Dispose();
            _target = null;
            throw;
        }
    }

    public SKImage Image
    {
        get
        {
            ObjectDisposedException.ThrowIf(_target is null, this);
            _target.VerifyAccess();
            return _snapshot.Image;
        }
    }

    public void Dispose()
    {
        if (_target is not { } target) return;
        target.VerifyAccess();
        _target = null;
        try { _snapshot.Dispose(); }
        finally { target.Dispose(); }
    }
}
