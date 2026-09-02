namespace Beutl.Graphics.Rendering;

public sealed class RawTargetScopeSession
{
    private readonly RenderExecutionSessionToken _token;
    private readonly ImmediateCanvas _canvas;
    private readonly Rect _outputBounds;
    private readonly RenderIntent _intent;
    private readonly RenderRequestPurpose _purpose;
    private readonly IReadOnlyList<RenderResourceBinding> _resourceBindings;
    private readonly IReadOnlyList<RenderResource> _resources;
    private readonly Action<ImmediateCanvas> _replayInput;
    private bool _replayed;

    internal RawTargetScopeSession(
        RenderExecutionSessionToken token,
        ImmediateCanvas canvas,
        Rect outputBounds,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        IReadOnlyList<RenderResourceBinding> resources,
        Action<ImmediateCanvas> replayInput)
    {
        _token = token;
        _canvas = canvas;
        _outputBounds = outputBounds;
        _intent = intent;
        _purpose = purpose;
        _resourceBindings = resources;
        _resources = resources.SelectToArray(static binding => binding.Resource);
        _replayInput = replayInput;
    }

    public ImmediateCanvas Canvas
    {
        get { _token.ThrowIfInactive(); return _canvas; }
    }

    public Rect OutputBounds
    {
        get { _token.ThrowIfInactive(); return _outputBounds; }
    }

    public RenderIntent Intent
    {
        get { _token.ThrowIfInactive(); return _intent; }
    }

    public RenderRequestPurpose Purpose
    {
        get { _token.ThrowIfInactive(); return _purpose; }
    }

    public void ReplayInput()
    {
        _token.ThrowIfInactive();
        if (_replayed)
            throw new InvalidOperationException("A raw target scope input must be replayed exactly once.");
        if (!_token.IsActiveCanvas(_canvas))
            throw new InvalidOperationException("ReplayInput must be called while the raw callback canvas is active.");

        _replayed = true;
        _replayInput(_canvas);
    }

    /// <summary>Uses the resource bound to a declared slot.</summary>
    /// <remarks>
    /// The addressing mode a reusable operation shape needs: its callback is static and its slots are fixed, so
    /// the token changes per call and only the slot names it from inside the callback.
    /// </remarks>
    public void UseResource<T>(RenderResourceSlot<T> slot, Action<T> use)
        where T : class
    {
        _token.UseResource(slot, _resourceBindings, use);
    }

    /// <summary>Uses a resource by its token.</summary>
    /// <remarks>For a request-local callback, which may capture the tokens it needs.</remarks>
    public void UseResource<T>(RenderResource<T> resource, Action<T> use)
        where T : class
    {
        _token.UseResource(resource, _resources, use);
    }

    internal void ValidateCompletion()
    {
        _token.ThrowIfInactive();
        if (!_replayed)
            throw new InvalidOperationException("A raw target scope input must be replayed exactly once.");
    }
}
