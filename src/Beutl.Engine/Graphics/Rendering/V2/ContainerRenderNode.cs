using System.Runtime.InteropServices;

namespace Beutl.Graphics.Rendering.V2;

public class ContainerRenderNode : RenderNode
{
    private readonly List<RenderNode> _children = [];

    public IReadOnlyList<RenderNode> Children => _children;

    public void AddChild(RenderNode item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _children.Add(item);
    }

    public void RemoveChild(RenderNode item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _children.Remove(item);
    }

    public void RemoveRange(int index, int count)
    {
        _children.RemoveRange(index, count);
    }

    public void SetChild(int index, RenderNode item)
    {
        _children[index]?.Dispose();
        _children[index] = item;
    }

    public void BringFrom(ContainerRenderNode containerNode)
    {
        _children.Clear();
        _children.AddRange(containerNode._children);

        containerNode._children.Clear();
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        return context.Input;
    }

    protected override void OnDispose(bool disposing)
    {
        foreach (RenderNode? item in CollectionsMarshal.AsSpan(_children))
        {
            item.Dispose();
        }

        _children.Clear();
    }
}
