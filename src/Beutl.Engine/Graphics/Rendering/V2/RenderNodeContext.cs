namespace Beutl.Graphics.Rendering.V2;

public class RenderNodeContext(IImmediateCanvasFactory canvasFactory, RenderNodeOperation[] input)
{
    public RenderNodeOperation[] Input { get; } = input;

    public IImmediateCanvasFactory CanvasFactory { get; } = canvasFactory;

    public bool IsRenderCacheEnabled { get; set; } = true;

    public Rect CalculateBounds()
    {
        return Input.Aggregate<RenderNodeOperation, Rect>(default, (current, operation) => current.Union(operation.Bounds));
    }
}
