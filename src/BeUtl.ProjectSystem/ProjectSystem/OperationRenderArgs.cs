using BeUtl.Rendering;

namespace BeUtl.ProjectSystem;

public readonly struct OperationRenderArgs
{
    public OperationRenderArgs(TimeSpan currentTime, IRenderer renderer, IScopedRenderable scope)
    {
        CurrentTime = currentTime;
        Renderer = renderer;
        Scope = scope;
    }

    public TimeSpan CurrentTime { get; }

    public IRenderer Renderer { get; }

    public IScopedRenderable Scope { get; }
}
