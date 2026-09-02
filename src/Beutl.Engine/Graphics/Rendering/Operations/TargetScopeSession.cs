namespace Beutl.Graphics.Rendering;

public sealed class TargetScopeSession
{
    private readonly RenderExecutionSessionToken _token;
    private readonly Rect _outputBounds;
    private readonly Rect _requiredRegion;
    private readonly RenderIntent _intent;
    private readonly RenderRequestPurpose _purpose;
    private readonly RenderCallbackCanvas _canvas;
    private readonly IReadOnlyList<RenderResourceBinding> _resourceBindings;
    private readonly IReadOnlyList<RenderResource> _resources;
    private readonly Action<ImmediateCanvas> _replayInput;
    private bool _replayed;

    internal TargetScopeSession(
        RenderExecutionSessionToken token,
        Rect outputBounds,
        Rect requiredRegion,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        RenderCallbackCanvas canvas,
        IReadOnlyList<RenderResourceBinding> resources,
        Action<ImmediateCanvas> replayInput)
    {
        _token = token;
        _outputBounds = outputBounds;
        _requiredRegion = requiredRegion;
        _intent = intent;
        _purpose = purpose;
        _canvas = canvas;
        _resourceBindings = resources;
        _resources = resources.SelectToArray(static binding => binding.Resource);
        _replayInput = replayInput;
    }

    public Rect OutputBounds
    {
        get { _token.ThrowIfInactive(); return _outputBounds; }
    }

    public Rect RequiredRegion
    {
        get { _token.ThrowIfInactive(); return _requiredRegion; }
    }

    public RenderIntent Intent
    {
        get { _token.ThrowIfInactive(); return _intent; }
    }

    public RenderRequestPurpose Purpose
    {
        get { _token.ThrowIfInactive(); return _purpose; }
    }

    public RenderCallbackCanvas Canvas
    {
        get { _token.ThrowIfInactive(); return _canvas; }
    }

    public void ReplayInput()
    {
        _token.ThrowIfInactive();
        if (_replayed)
            throw new InvalidOperationException("A target scope input must be replayed exactly once.");

        ImmediateCanvas canvas = _token.GetActiveCanvas(_canvas);
        _replayed = true;
        canvas.ReplayTargetScopeInput(_replayInput);
    }

    /// <summary>Uses the resource bound to a declared slot.</summary>
    public void UseResource<T>(RenderResourceSlot<T> slot, Action<T> use)
        where T : class
    {
        _token.UseResource(slot, _resourceBindings, use);
    }

    internal void ValidateCompletion()
    {
        _token.ThrowIfInactive();
        if (!_replayed)
            throw new InvalidOperationException("A target scope input must be replayed exactly once.");
    }
}
