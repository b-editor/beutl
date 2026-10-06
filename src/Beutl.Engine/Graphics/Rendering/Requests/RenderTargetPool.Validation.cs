using Beutl.Media;

using SkiaSharp;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RenderTargetPool
{
    /// <summary>
    /// Whether <paramref name="target"/>'s backing surface is one this pool or the request already holds.
    /// </summary>
    /// <remarks>
    /// A factory can hand back a fresh target instance wrapping a surface something else is still drawing to.
    /// Rejecting it is right, but disposing it would take that surface down with it and leave a live pool slot
    /// or the caller's destination pointing at freed memory, so <see cref="ReleaseRejectedWrapper"/> settles
    /// the rejection instead.
    /// </remarks>
    private bool SharesLiveSurface(RenderTarget target, RenderTargetLeaseSession request)
    {
        try
        {
            SKSurface surface = target.RawValue;
            return ReferenceEquals(surface, request.ExternalSurface) || _knownSurfaces.Contains(surface);
        }
        catch
        {
            // A target that cannot even show its surface shares nothing, so the caller owns its disposal.
            return false;
        }
    }

    private SKSurface ValidateFactoryTarget(
        RenderTarget target,
        PixelSize size,
        RenderTargetLeaseSession request)
    {
        if (ReferenceEquals(target, request.ExternalTarget))
        {
            throw new InvalidOperationException(
                "The render-target factory returned the borrowed destination as an owned allocation.");
        }
        if (_knownTargets.Contains(target))
        {
            throw new InvalidOperationException(
                "The render-target factory returned a target instance already owned by this pool.");
        }

        SKSurface surface = ValidateNewSurface(target, size);
        if (ReferenceEquals(surface, request.ExternalSurface) || _knownSurfaces.Contains(surface))
        {
            throw new InvalidOperationException(
                "The render-target factory returned a backing surface that is already in use.");
        }

        ValidateContext(surface, request);
        return surface;
    }

    private void ValidateReusableSlot(TargetSlot slot, RenderTargetLeaseSession request)
    {
        if (!_ownedSlots.Contains(slot)
            || slot.ActiveLease is not null
            || slot.Target.IsDisposed)
        {
            throw new InvalidOperationException("The pooled render target is no longer reusable.");
        }

        SKSurface surface = ValidateSurfaceIdentityAndViewport(slot.Target, slot.Size);
        if (!ReferenceEquals(surface, slot.Surface))
            throw new InvalidOperationException("A pooled render target changed its backing surface.");
        ValidateContext(surface, request);
    }

    private static SKSurface ValidateNewSurface(RenderTarget target, PixelSize size)
    {
        SKSurface surface = ValidateSurfaceIdentityAndViewport(target, size);
        // Snapshot is a GPU read, not a metadata query: on Vulkan it changes Skia's private image
        // layout before a native pass writes the target. Trust the engine's recorded creation format;
        // retain inspection for caller-supplied surfaces whose format is not known.
        if (target.KnownPixelFormat == RenderTargetPixelFormat.LinearPremultipliedRgba16Float)
            return surface;

        using SKImage? image = surface.Snapshot();
        using SKColorSpace expectedColorSpace = SKColorSpace.CreateSrgbLinear();
        using SKColorSpace? actualColorSpace = image?.ColorSpace;
        if (image is null
            || image.Width != size.Width
            || image.Height != size.Height
            || image.ColorType != SKColorType.RgbaF16
            || image.AlphaType != SKAlphaType.Premul
            || actualColorSpace is null
            || !SKColorSpace.Equal(actualColorSpace, expectedColorSpace))
        {
            throw new InvalidOperationException(
                "Pooled render targets must be linear-premultiplied RGBA16F surfaces.");
        }

        return surface;
    }

    private static SKSurface ValidateSurfaceIdentityAndViewport(RenderTarget target, PixelSize size)
    {
        if (target.IsDisposed || target.Width != size.Width || target.Height != size.Height)
        {
            throw new InvalidOperationException(
                "The render-target factory returned a disposed target or a target whose exact device size is wrong.");
        }

        target.VerifyAccess();
        SKSurface surface = target.RawValue;
        SKRectI deviceClip = surface.Canvas.DeviceClipBounds;
        if (deviceClip.Left != 0
            || deviceClip.Top != 0
            || deviceClip.Width != size.Width
            || deviceClip.Height != size.Height)
        {
            throw new InvalidOperationException(
                "The render-target surface has an incompatible device viewport.");
        }

        return surface;
    }

    private void ValidateContext(SKSurface surface, RenderTargetLeaseSession request)
    {
        GRRecordingContext? actualContext = surface.Context;
        nint actual = actualContext?.Handle ?? 0;
        if (request.ExpectedContextHandle is { } expected && actual != expected)
        {
            throw new InvalidOperationException(
                "The render-target factory returned a target from an incompatible graphics context.");
        }

        if (!_hasContext)
        {
            _contextIdentity = request.ContextIdentity;
            _contextHandle = actual;
            _hasContext = true;
        }
        else if (!ReferenceEquals(_contextIdentity, request.ContextIdentity)
                 || _contextHandle != actual)
        {
            throw new InvalidOperationException(
                "The render-target factory returned targets from incompatible graphics contexts.");
        }

        _graphicsContext = actualContext;
    }

    private void VerifyActive(RenderTargetLeaseSession request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        if (!ReferenceEquals(_activeSession, request) || request.IsDisposed)
            throw new InvalidOperationException("The render-target allocation session is no longer active.");
    }

    internal void VerifyLease(RenderTargetLease lease)
    {
        VerifyPoolOwnership(lease);
        if (lease.State != RenderTargetLeaseState.Leased)
        {
            throw new InvalidOperationException(
                $"The render-target lease has already been discharged as {lease.State}.");
        }

        VerifySlotHolds(lease);
    }

    private void VerifyReleasableLease(RenderTargetLease lease)
    {
        VerifyPoolOwnership(lease);
        if (lease.State is not (RenderTargetLeaseState.Leased or RenderTargetLeaseState.ReleaseFailed))
        {
            throw new InvalidOperationException(
                $"The render-target lease has already been discharged as {lease.State}.");
        }

        VerifySlotHolds(lease);
    }

    private void VerifyPoolOwnership(RenderTargetLease lease)
    {
        if (!ReferenceEquals(lease.Session.Pool, this))
            throw new InvalidOperationException("The render-target lease belongs to a different pool.");
    }

    // A lease is stale once its slot holds a different lease, or none.
    private static void VerifySlotHolds(RenderTargetLease lease)
    {
        TargetSlot slot = lease.Slot;
        if (!ReferenceEquals(slot.ActiveLease, lease))
            throw new InvalidOperationException("The render-target lease is stale.");
    }

    private bool IsCurrentContext(RenderTargetLeaseSession request)
        => _hasContext
           && ReferenceEquals(_contextIdentity, request.ContextIdentity)
           && _contextGeneration == request.ContextGeneration;
}
