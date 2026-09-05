namespace Beutl.Graphics.Rendering;

public sealed class TargetScopeSession
{
    private readonly Rect _outputBounds;
    private readonly RenderIntent _intent;
    private readonly RenderRequestPurpose _purpose;
    private readonly RenderCallbackCanvas _canvas;
    private readonly IReadOnlyList<RenderResourceBinding> _resourceBindings;
    private readonly Action<ImmediateCanvas> _replayInput;
    private bool _replayed;

    internal TargetScopeSession(
        Rect outputBounds,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        RenderCallbackCanvas canvas,
        IReadOnlyList<RenderResourceBinding> resources,
        Action<ImmediateCanvas> replayInput)
    {
        _outputBounds = outputBounds;
        _intent = intent;
        _purpose = purpose;
        _canvas = canvas;
        _resourceBindings = resources;
        _replayInput = replayInput;
    }

    public Rect OutputBounds
    {
        get { _canvas.Token.ThrowIfInactive(); return _outputBounds; }
    }

    public Rect RequiredRegion => _canvas.LogicalBounds;

    public RenderIntent Intent
    {
        get { _canvas.Token.ThrowIfInactive(); return _intent; }
    }

    public RenderRequestPurpose Purpose
    {
        get { _canvas.Token.ThrowIfInactive(); return _purpose; }
    }

    public RenderCallbackCanvas Canvas
    {
        get { _canvas.Token.ThrowIfInactive(); return _canvas; }
    }

    public void ReplayInput()
    {
        _canvas.Token.ThrowIfInactive();
        if (_replayed)
            throw new InvalidOperationException("A target scope input must be replayed exactly once.");

        ImmediateCanvas canvas = _canvas.Token.GetActiveCanvas(_canvas);
        _replayed = true;
        canvas.ReplayTargetScopeInput(_replayInput);
    }

    /// <summary>Uses the resource bound to a declared slot.</summary>
    public void UseResource<T>(RenderResourceSlot<T> slot, Action<T> use)
        where T : class
    {
        _canvas.Token.UseResource(slot, _resourceBindings, use);
    }

    internal void ValidateCompletion()
    {
        _canvas.Token.ThrowIfInactive();
        if (!_replayed)
            throw new InvalidOperationException("A target scope input must be replayed exactly once.");
    }
}
