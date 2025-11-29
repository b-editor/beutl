namespace Beutl.Graphics.Rendering;

public class MemoryNode<T>(T value) : RenderNode
{
    public T Value { get; set; } = value;

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        return [];
    }
}
