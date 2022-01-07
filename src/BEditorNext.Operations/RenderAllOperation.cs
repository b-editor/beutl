using BEditorNext.ProjectSystem;
using BEditorNext.Rendering;

namespace BEditorNext.Operations;

public sealed class RenderAllOperation : RenderOperation
{
    public static readonly PropertyDefine<bool> DeleteRenderedObjectsProperty;
    public static readonly PropertyDefine<int> StartProperty;
    public static readonly PropertyDefine<int> CountProperty;

    static RenderAllOperation()
    {
        DeleteRenderedObjectsProperty = RegisterProperty<bool, RenderAllOperation>(nameof(DeleteRenderedObjects), o => o.DeleteRenderedObjects, (o, v) => o.DeleteRenderedObjects = v)
            .Header("DeleteRenderedObjectsString")
            .EnableEditor()
            .DefaultValue(true)
            .JsonName("deleteRendered");

        StartProperty = RegisterProperty<int, RenderAllOperation>(nameof(Start), o => o.Start, (o, v) => o.Start = v)
            .Header("StartIndexString")
            .EnableEditor()
            .DefaultValue(0)
            .Minimum(0)
            .JsonName("start");

        CountProperty = RegisterProperty<int, RenderAllOperation>(nameof(Count), o => o.Count, (o, v) => o.Count = v)
            .Header("CountString")
            .EnableEditor()
            .DefaultValue(-1)
            .Minimum(-1)
            .JsonName("count");
    }

    public bool DeleteRenderedObjects { get; set; } = true;

    public int Start { get; set; }

    public int Count { get; set; } = -1;

    public override void Render(in OperationRenderArgs args)
    {
        int start = Math.Max(Start, 0);
        int count = Count;
        if (count < 0)
        {
            count = args.List.Count;
        }

        for (int i = start; i < count; i++)
        {
            IRenderable item = args.List[i];
            item.Render(args.Renderer);
        }

        if (DeleteRenderedObjects)
        {
            for (int i = start; i < count; i++)
            {
                IRenderable item = args.List[i];
                args.List.RemoveAt(i);
                item.Dispose();
                i--;
                count--;
            }
        }
    }
}
