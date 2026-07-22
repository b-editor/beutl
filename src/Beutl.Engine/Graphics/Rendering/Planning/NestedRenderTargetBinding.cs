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
    private RenderPipelineDiagnosticRecorder? _diagnostics;
    private NestedRenderTargetBindingState _state;

    public Rect LogicalBounds { get; private set; }

    public float Density { get; private set; }

    public PixelRect DeviceBounds { get; private set; }

    public bool IsReady => _state == NestedRenderTargetBindingState.Ready;

    public bool IsDisposed => _state == NestedRenderTargetBindingState.Disposed;

    public void Stage(
        RenderTargetLease lease,
        Rect logicalBounds,
        float density,
        RenderPipelineDiagnosticRecorder? diagnostics)
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
        _diagnostics = diagnostics;
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

        _lease.Target.PrepareForSampling();
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
        try
        {
            lease?.Dispose();
        }
        finally
        {
            if (lease is not null)
                _diagnostics?.RecordIntermediateDischarged();
            _diagnostics = null;
        }
    }
}

internal sealed class NestedRenderTargetImage
{
    private readonly RenderExecutionSessionToken _token;
    private readonly SKImage _image;

    public NestedRenderTargetImage(
        RenderExecutionSessionToken token,
        SKImage image,
        Rect logicalBounds,
        float density,
        PixelRect deviceBounds)
    {
        _token = token;
        _image = image;
        LogicalBounds = logicalBounds;
        Density = density;
        DeviceBounds = deviceBounds;
    }

    public Rect LogicalBounds
    {
        get { _token.ThrowIfInactive(); return field; }
    }

    public float Density
    {
        get { _token.ThrowIfInactive(); return field; }
    }

    public PixelRect DeviceBounds
    {
        get { _token.ThrowIfInactive(); return field; }
    }

    public Rect RasterBounds
    {
        get
        {
            _token.ThrowIfInactive();
            return DeviceBounds.ToRect(Density);
        }
    }

    public void Draw(ImmediateCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        _token.VerifyActiveCanvas(canvas);
        canvas.DrawImageScaled(_image, RasterBounds);
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

internal static class NestedRenderTargetBindingScope
{
    private static readonly AsyncLocal<Scope?> s_current = new();

    public static void Use(object identity, NestedRenderTargetBinding binding, Action use)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(use);

        Scope? previous = s_current.Value;
        s_current.Value = new Scope(identity, binding, previous);
        try
        {
            use();
        }
        finally
        {
            s_current.Value = previous;
        }
    }

    public static bool TryGet(object identity, out NestedRenderTargetBinding binding)
    {
        ArgumentNullException.ThrowIfNull(identity);
        for (Scope? current = s_current.Value; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current.Identity, identity))
            {
                binding = current.Binding;
                return true;
            }
        }

        binding = null!;
        return false;
    }

    private sealed record Scope(
        object Identity,
        NestedRenderTargetBinding Binding,
        Scope? Parent);
}

internal sealed record RecordedNestedRenderTarget(
    RecordedNestedRenderRequest Recording,
    RenderResource<NestedRenderTargetBinding> Binding,
    NestedRenderTargetBinding Target)
{
    public RenderRequest Request => Recording.Request;

    public RecordedRenderGraph Graph => Recording.Graph;
}
