using Beutl.Composition;
using Beutl.Engine;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

/// <summary>Callback-scoped access to dependencies and explicitly declared composition-target work.</summary>
public sealed class RenderExecutionContext : IDisposable
{
    private Func<RenderNode, object, Rect, float, RenderTargetImageLease>? _render;
    private ImmediateCanvas? _target;
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private bool _disposed;

    internal RenderExecutionContext(
        Func<RenderNode, object, Rect, float, RenderTargetImageLease> render,
        float workingScale, float maxWorkingScale, ImmediateCanvas? target = null)
    {
        _render = render;
        WorkingScale = workingScale;
        MaxWorkingScale = maxWorkingScale;
        _target = target;
    }

    internal RenderExecutionContext CreateCallbackScope()
    {
        VerifyAccess();
        return new RenderExecutionContext(_render!, WorkingScale, MaxWorkingScale, _target);
    }

    public float WorkingScale { get; }
    public float MaxWorkingScale { get; }
    public bool CanAccessTarget => _target is not null;

    /// <summary>Evaluates a dynamic dependency through the current request family and target allocator.</summary>
    /// <remarks>
    /// The identity must be immutable and identify the evaluated content and time within this frame.
    /// The caller retains the node and its resources through the call, and owns the returned image lease.
    /// Repeated identities with the same domain and density reuse the completed dependency in this request.
    /// </remarks>
    public RenderTargetImageLease RenderDependency(RenderNode root, object identity, Rect domain)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(identity);
        return _render!(root, identity, domain, MathF.Min(WorkingScale, MaxWorkingScale));
    }

    /// <summary>Records the evaluated drawables into a dependency without constructing another renderer.</summary>
    public RenderTargetImageLease RenderDependency(CompositionFrame frame, object identity)
    {
        VerifyAccess();
        using var nodes = new ContainerRenderNode();
        foreach (EngineObject.Resource resource in frame.Objects)
        {
            if (resource is not Drawable.Resource drawable) continue;
            var node = new DrawableRenderNode(drawable);
            nodes.AddChild(node);
            using var context = new GraphicsContext2D(node, frame.Size.ToSize(1), WorkingScale);
            drawable.RequireOriginal().Render(context, drawable);
        }
        return RenderDependency(nodes, identity, new Rect(default, frame.Size.ToSize(1)));
    }

    /// <summary>Samples the preceding composition target without a CPU pixel readback.</summary>
    public RenderTargetImageLease CaptureTarget()
    {
        ImmediateCanvas target = RequireTarget();
        using RenderTarget surface = RenderTarget.GetRenderTarget(target);
        return surface.AcquireImage();
    }

    /// <summary>Replaces the declared composition target, including transparent pixels.</summary>
    public void ReplaceTarget(SKImage image)
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
        if (Environment.CurrentManagedThreadId != _threadId)
            throw new InvalidOperationException("Render execution access belongs to the callback thread.");
    }

    public void Dispose()
    {
        _disposed = true;
        _render = null;
        _target = null;
    }
}
