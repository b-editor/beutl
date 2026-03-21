namespace Beutl.Graphics.Rendering;

public class OperationWrapperRenderNode : RenderNode
{
    private RenderNodeOperation[] _operations = [];

    public void SetOperations(RenderNodeOperation[] operations)
    {
        _operations = operations;
        HasChanges = true;
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        return _operations;
    }
}
