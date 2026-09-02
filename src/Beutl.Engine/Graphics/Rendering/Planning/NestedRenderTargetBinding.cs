using Beutl.Graphics.Backend;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Owns the target produced by one nested render request until its request family completes.
/// The binding is created while recording, populated only by the executor, and can be consumed
/// only through a request-declared resource scope.
/// </summary>
internal sealed class NestedRenderTargetBinding : IDisposable
{
    private RenderTargetLease? _lease;
    private NestedRenderTargetBindingState _state;

    public Rect LogicalBounds { get; private set; }

    public float Density { get; private set; }

    public PixelRect DeviceBounds { get; private set; }

    public bool IsReady => _state == NestedRenderTargetBindingState.Ready;

    public bool IsDisposed => _state == NestedRenderTargetBindingState.Disposed;

    public void Stage(
        RenderTargetLease lease,
        Rect logicalBounds,
        float density)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_state != NestedRenderTargetBindingState.Empty)
            throw new InvalidOperationException("A nested render target can be staged only once.");
        RenderDescriptionValidation.ThrowIfFiniteNonEmpty(logicalBounds, nameof(logicalBounds));
        if (!float.IsFinite(density) || density <= 0)
            throw new ArgumentOutOfRangeException(nameof(density));

        PixelRect deviceBounds = PixelRect.FromRect(logicalBounds, density);
        if (deviceBounds.Size != new PixelSize(lease.Target.Width, lease.Target.Height))
        {
            throw new ArgumentException(
                "The nested target lease does not match the declared logical bounds and density.",
                nameof(lease));
        }

        _lease = lease;
        LogicalBounds = logicalBounds;
        Density = density;
        DeviceBounds = deviceBounds;
        _state = NestedRenderTargetBindingState.Staged;
    }

    public void PrepareForSampling()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_state != NestedRenderTargetBindingState.Staged || _lease is null)
            throw new InvalidOperationException("The nested render target is not staged.");

        // The binding can later expose either an SKImage or its Vulkan texture. Until the consumer is known,
        // retain the cross-backend synchronization required by the latter.
        _lease.Target.PrepareForSampling(RenderTargetSamplingIntent.BackendInterop);
        _state = NestedRenderTargetBindingState.Ready;
    }

    public void Reject()
    {
        if (_state is NestedRenderTargetBindingState.Disposed or NestedRenderTargetBindingState.Empty)
            return;

        _state = NestedRenderTargetBindingState.Rejected;
    }

    public ITexture2D? GetTexture(Rect expectedLogicalBounds, float expectedDensity)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        RenderDescriptionValidation.ThrowIfFiniteNonEmpty(expectedLogicalBounds, nameof(expectedLogicalBounds));
        if (!float.IsFinite(expectedDensity) || expectedDensity <= 0f)
            throw new ArgumentOutOfRangeException(nameof(expectedDensity));
        if (_state is NestedRenderTargetBindingState.Empty or NestedRenderTargetBindingState.Rejected)
            return null;
        if (_state != NestedRenderTargetBindingState.Ready || _lease is null)
            throw new InvalidOperationException("The nested render target is not ready for sampling.");
        if (LogicalBounds != expectedLogicalBounds)
        {
            throw new InvalidOperationException(
                "The prepared nested target does not match the texture source's logical domain.");
        }
        if (Density != expectedDensity)
        {
            throw new InvalidOperationException(
                "The prepared nested target density does not match the consuming 3D surface density.");
        }

        return _lease.Target.Texture;
    }

    public void UseImage(
        RenderExecutionSessionToken token,
        Action<NestedRenderTargetImage> use)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(use);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_state != NestedRenderTargetBindingState.Ready || _lease is null)
            throw new InvalidOperationException("The nested render target is not ready for consumption.");

        using SKImage image = _lease.Target.Value.Snapshot();
        var view = new NestedRenderTargetImage(token, image, LogicalBounds, Density, DeviceBounds);
        token.AuthorizeResource(image, () => use(view));
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        RenderTargetLease? lease = Interlocked.Exchange(ref _lease, null);
        _state = NestedRenderTargetBindingState.Disposed;
        lease?.Dispose();
    }
}

internal enum NestedRenderTargetBindingState : byte
{
    Empty,
    Staged,
    Ready,
    Rejected,
    Disposed,
}
