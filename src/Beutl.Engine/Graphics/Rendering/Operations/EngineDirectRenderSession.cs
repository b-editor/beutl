namespace Beutl.Graphics.Rendering;

internal sealed class EngineDirectRenderSession
{
    private readonly RenderExecutionSessionToken _token;

    internal EngineDirectRenderSession(
        RenderExecutionSessionToken token,
        ImmediateCanvas canvas)
    {
        _token = token;
        Canvas = canvas;
    }

    internal ImmediateCanvas Canvas { get; }

    internal RenderExecutionSessionToken Token => _token;
}
