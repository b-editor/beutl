using BeUtl.Rendering;

namespace BeUtl.ProjectSystem;

public readonly struct OperationRenderArgs
{
    public OperationRenderArgs(TimeSpan currentTime, IRenderer renderer, RenderableList list)
    {
        CurrentTime = currentTime;
        Renderer = renderer;
        List = list;
    }

    public TimeSpan CurrentTime { get; }

    public IRenderer Renderer { get; }

    public RenderableList List { get; }
}
