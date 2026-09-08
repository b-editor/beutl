namespace Beutl.Graphics.Rendering;

public sealed class OpaqueRenderOutput : IDisposable
{
    private readonly OpaqueRenderSession _owner;
    private readonly RenderCallbackCanvas _canvas;
    private readonly Action<OpaqueRenderOutput>? _release;
    private Rect _bounds;
    private OpaqueRenderOutputState _state;

    internal OpaqueRenderOutput(
        OpaqueRenderSession owner,
        RenderCallbackCanvas canvas,
        Action<OpaqueRenderOutput>? release = null)
    {
        _owner = owner;
        _bounds = canvas.LogicalBounds;
        _canvas = canvas;
        _release = release;
    }

    public Rect Bounds
    {
        get { ThrowIfUnavailable(); return _bounds; }
    }

    public EffectiveScale EffectiveScale
    {
        get { ThrowIfUnavailable(); return EffectiveScale.At(_canvas.Density); }
    }

    public RenderCallbackCanvas Canvas
    {
        get { ThrowIfUnavailable(); return _canvas; }
    }

    public void SetOutputBounds(Rect logicalBounds)
    {
        ThrowIfUnavailable();
        RenderRectValidation.ThrowIfInvalidInput(logicalBounds, nameof(logicalBounds));
        if (!_canvas.LogicalBounds.Contains(logicalBounds))
        {
            throw new ArgumentException(
                "Output bounds may only shrink within the allocated output bounds.",
                nameof(logicalBounds));
        }

        _bounds = logicalBounds;
    }

    public void Discard()
    {
        ThrowIfUnavailable();
        _state = OpaqueRenderOutputState.Discarded;
        _release?.Invoke(this);
    }

    public void Dispose()
    {
        _owner.Token.ThrowIfInactive();
        if (_state != OpaqueRenderOutputState.Active)
            return;

        _state = OpaqueRenderOutputState.Disposed;
        _release?.Invoke(this);
    }

    internal void Publish(OpaqueRenderSession owner, Action<OpaqueRenderOutput> publish)
    {
        ThrowIfUnavailable();
        if (!ReferenceEquals(owner, _owner))
            throw new InvalidOperationException("An opaque output belongs to a different execution session.");

        publish(this);
        _state = OpaqueRenderOutputState.Published;
    }

    private void ThrowIfUnavailable()
    {
        _owner.Token.ThrowIfInactive();
        if (_state != OpaqueRenderOutputState.Active)
            throw new InvalidOperationException("The opaque output lease is no longer active.");
    }
}
