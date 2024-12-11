namespace Beutl.Graphics.Rendering;

public class RenderNodeContext(RenderNodeOperation[] input)
{
    public RenderNodeOperation[] Input { get; } = input;

    public bool IsRenderCacheEnabled { get; set; } = true;

    public Rect CalculateBounds()
    {
        return Input.Aggregate<RenderNodeOperation, Rect>(default, (current, operation) => current.Union(operation.Bounds));
    }
}
