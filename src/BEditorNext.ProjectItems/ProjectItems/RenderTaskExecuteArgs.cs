using BEditorNext.Graphics;

namespace BEditorNext.ProjectItems;

public readonly struct RenderTaskExecuteArgs
{
    public RenderTaskExecuteArgs(TimeSpan currentTime, IRenderer renderer, RenderableList list)
    {
        CurrentTime = currentTime;
        Renderer = renderer;
        List = list;
    }

    public TimeSpan CurrentTime { get; }

    public IRenderer Renderer { get; }

    public RenderableList List { get; }
}
