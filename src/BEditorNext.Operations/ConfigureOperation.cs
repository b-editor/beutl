using BEditorNext.ProjectSystem;
using BEditorNext.Rendering;

namespace BEditorNext.Operations;

public abstract class ConfigureOperation : RenderOperation
{
    public override void Render(in OperationRenderArgs args)
    {
        for (int i = 0; i < args.List.Count; i++)
        {
            IRenderable item = args.List[i];
            Configure(args, ref item);
            args.List[i] = item;
        }
    }

    public abstract void Configure(in OperationRenderArgs args, ref IRenderable obj);
}

public abstract class ConfigureOperation<T> : ConfigureOperation
    where T : IRenderable
{
    public override void Configure(in OperationRenderArgs args, ref IRenderable obj)
    {
        if (obj is T typed)
        {
            Configure(args, ref typed);
            obj = typed;
        }
    }

    public abstract void Configure(in OperationRenderArgs args, ref T obj);
}
