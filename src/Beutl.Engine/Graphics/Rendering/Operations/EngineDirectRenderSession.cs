namespace Beutl.Graphics.Rendering;

internal sealed class EngineDirectRenderSession
{
    private readonly RenderExecutionSessionToken _token;
    private readonly IReadOnlyList<RenderExecutionInput> _inputs;

    internal EngineDirectRenderSession(
        RenderExecutionSessionToken token,
        ImmediateCanvas canvas,
        IReadOnlyList<RenderExecutionInput> inputs)
    {
        _token = token;
        Canvas = canvas;
        _inputs = inputs;
    }

    internal ImmediateCanvas Canvas { get; }

    internal RenderExecutionSessionToken Token => _token;

    internal IReadOnlyList<RenderExecutionInput> Inputs
    {
        get { _token.ThrowIfInactive(); return _inputs; }
    }
}
