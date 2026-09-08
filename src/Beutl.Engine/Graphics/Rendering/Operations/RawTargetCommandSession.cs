namespace Beutl.Graphics.Rendering;

public sealed class RawTargetCommandSession
{
    private readonly RenderExecutionSessionToken _token;
    private readonly ImmediateCanvas _canvas;
    private readonly RenderIntent _intent;
    private readonly RenderRequestPurpose _purpose;
    private readonly IReadOnlyList<RenderResourceBinding> _resourceBindings;

    internal RawTargetCommandSession(
        RenderExecutionSessionToken token,
        ImmediateCanvas canvas,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        IReadOnlyList<RenderResourceBinding> resources)
    {
        _token = token;
        _canvas = canvas;
        _intent = intent;
        _purpose = purpose;
        _resourceBindings = resources;
    }

    public ImmediateCanvas Canvas
    {
        get { _token.ThrowIfInactive(); return _canvas; }
    }

    public RenderIntent Intent
    {
        get { _token.ThrowIfInactive(); return _intent; }
    }

    public RenderRequestPurpose Purpose
    {
        get { _token.ThrowIfInactive(); return _purpose; }
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
        _token.UseResource(resource, _resourceBindings, use);
    }
}
