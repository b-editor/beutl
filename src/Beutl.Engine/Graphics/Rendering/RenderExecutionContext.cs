using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

/// <summary>Callback-scoped access to dependencies rendered through the active request.</summary>
/// <remarks>The renderer ends it when the callback returns; later calls throw <see cref="ObjectDisposedException"/>.</remarks>
public sealed class RenderExecutionContext
{
    private Func<RenderNode, object?, object, Rect, float, RenderTargetImageLease>? _render;
    private ImmediateCanvas? _target;
    // The recorded effect calling back; dependencies are cached per owner so equal identities never cross effects.
    private readonly object? _owner;
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private bool _disposed;

    internal RenderExecutionContext(
        Func<RenderNode, object?, object, Rect, float, RenderTargetImageLease> render,
        float workingScale, float maxWorkingScale, ImmediateCanvas? target = null, CancellationToken cancellationToken = default,
        object? owner = null)
    {
        _render = render;
        _owner = owner;
        CancellationToken = cancellationToken;
        WorkingScale = workingScale;
        MaxWorkingScale = maxWorkingScale;
        _target = target;
    }

    internal RenderExecutionContext CreateCallbackScope(float workingScale, object owner)
    {
        VerifyAccess();
        return new RenderExecutionContext(_render!, workingScale, MaxWorkingScale, _target, CancellationToken, owner);
    }

    public CancellationToken CancellationToken { get; }

    public float WorkingScale { get; }
    public float MaxWorkingScale { get; }
    internal bool CanAccessTarget => _target is not null;

    /// <summary>Evaluates a dynamic dependency through the current request family and target allocator.</summary>
    /// <remarks>
    /// The identity must be immutable and identify the evaluated content and time within this frame.
    /// The caller retains the node and its resources through the call, and owns the returned image lease.
    /// Repeated identities from the same recorded effect with the same domain and density reuse the completed
    /// dependency in this request; another effect never receives it, even with an equal identity. A dependency
    /// that re-enters the same identity from the same effect, or nests more than 32 deep, fails as circular.
    /// </remarks>
    public RenderTargetImageLease RenderDependency(RenderNode root, object identity, Rect domain)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(identity);
        return _render!(root, _owner, identity, domain, MathF.Min(WorkingScale, MaxWorkingScale));
    }

    /// <summary>Records the evaluated drawables into a dependency without constructing another renderer.</summary>
    public RenderTargetImageLease RenderDependency(CompositionFrame frame, object identity)
    {
        VerifyAccess();
        Size size = frame.Size.ToSize(1);
        using var nodes = new ContainerRenderNode();
        using (var context = new GraphicsContext2D(nodes, size, WorkingScale))
        {
            foreach (EngineObject.Resource resource in frame.Objects)
            {
                if (resource is Drawable.Resource drawable)
                    context.DrawDrawable(drawable);
            }
        }
        return RenderDependency(nodes, identity, new Rect(default, size));
    }

    /// <summary>Renders a time-sampled input using this request's allocation, density and cache policy.</summary>
    /// <remarks>Domain must be finite and non-empty, in the drawable's parent coordinates, and cover all input needed by the caller.</remarks>
    public RenderTargetImageLease RenderDependency(FilterEffectInputSample sample, Size availableSize, Rect domain)
    {
        ArgumentNullException.ThrowIfNull(sample);
        VerifyAccess();
        using var root = sample.CreateRenderNode(availableSize, WorkingScale);
        return RenderDependency(root, (sample, availableSize), domain);
    }

    internal RenderTargetImageLease CaptureTarget()
    {
        ImmediateCanvas target = RequireTarget();
        using RenderTarget surface = RenderTarget.GetRenderTarget(target);
        return surface.AcquireImage();
    }

    internal void ReplaceTarget(SKImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        ImmediateCanvas target = RequireTarget();
        using (target.PushDeviceSpace())
        using (var paint = new SKPaint { BlendMode = SKBlendMode.Src })
            target.Canvas.DrawImage(image, new SKRect(0, 0, target.DeviceSize.Width, target.DeviceSize.Height),
                SKSamplingOptions.Default, paint);
    }

    private ImmediateCanvas RequireTarget()
    {
        VerifyAccess();
        return _target ?? throw new InvalidOperationException(
            "This operation did not declare composition-target access while recording.");
    }

    private void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancellationToken.ThrowIfCancellationRequested();
        if (Environment.CurrentManagedThreadId != _threadId)
            throw new InvalidOperationException("Render execution access belongs to the callback thread.");
    }

    internal void End()
    {
        _disposed = true;
        _render = null;
        _target = null;
    }
}
