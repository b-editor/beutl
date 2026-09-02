namespace Beutl.Graphics.Rendering;

public sealed class OpaqueRenderOutput : IDisposable
{
    private readonly RenderExecutionSessionToken _token;
    private readonly OpaqueRenderSession _owner;
    private readonly Rect _allocationBounds;
    private readonly EffectiveScale _effectiveScale;
    private readonly RenderCallbackCanvas _canvas;
    private readonly Action<OpaqueRenderOutput>? _release;
    private Rect _bounds;
    private OpaqueRenderOutputState _state;

    internal OpaqueRenderOutput(
        RenderExecutionSessionToken token,
        OpaqueRenderSession owner,
        Rect bounds,
        EffectiveScale effectiveScale,
        RenderCallbackCanvas canvas,
        Action<OpaqueRenderOutput>? release = null)
    {
        _token = token;
        _owner = owner;
        _allocationBounds = bounds;
        _bounds = bounds;
        _effectiveScale = effectiveScale;
        _canvas = canvas;
        _release = release;
    }

    public Rect Bounds
    {
        get { ThrowIfUnavailable(); return _bounds; }
    }

    public EffectiveScale EffectiveScale
    {
        get { ThrowIfUnavailable(); return _effectiveScale; }
    }

    public RenderCallbackCanvas Canvas
    {
        get { ThrowIfUnavailable(); return _canvas; }
    }

    public void SetOutputBounds(Rect logicalBounds)
    {
        ThrowIfUnavailable();
        RenderRectValidation.ThrowIfInvalidInput(logicalBounds, nameof(logicalBounds));
        if (!_allocationBounds.Contains(logicalBounds))
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
        _token.ThrowIfInactive();
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
        _token.ThrowIfInactive();
        if (_state != OpaqueRenderOutputState.Active)
            throw new InvalidOperationException("The opaque output lease is no longer active.");
    }
}
