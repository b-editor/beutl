using BeUtl.Rendering;

namespace BeUtl.ProjectSystem;

public struct OperationRenderArgs
{
    public OperationRenderArgs(IRenderer renderer)
    {
        Renderer = renderer;
        Result = null;
    }

    public IRenderer Renderer { get; }

    public Renderable? Result { readonly get; set; }
}
