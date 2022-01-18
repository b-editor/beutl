using BeUtl.Graphics;
using BeUtl.ProjectSystem;
using BeUtl.Rendering;

namespace BeUtl.Operations;

public sealed class RenderAllOperation : LayerOperation
{
    public static readonly CoreProperty<int> StartProperty;
    public static readonly CoreProperty<int> CountProperty;

    static RenderAllOperation()
    {
        StartProperty = ConfigureProperty<int, RenderAllOperation>(nameof(Start))
            .Accessor(o => o.Start, (o, v) => o.Start = v)
            .Header("StartIndexString")
            .EnableEditor()
            .DefaultValue(0)
            .Minimum(0)
            .JsonName("start")
            .Register();

        CountProperty = ConfigureProperty<int, RenderAllOperation>(nameof(Count))
            .Accessor(o => o.Count, (o, v) => o.Count = v)
            .Header("CountString")
            .EnableEditor()
            .DefaultValue(-1)
            .Minimum(-1)
            .JsonName("count")
            .Register();
    }

    public int Start { get; set; }

    public int Count { get; set; } = -1;

    public override void ApplySetters(in OperationRenderArgs args)
    {
        base.ApplySetters(args);
        int start = Math.Max(Start, 0);
        int count = Count;
        if (count < 0)
        {
            count = args.Scope.Count;
        }

        for (int i = start; i < count; i++)
        {
            IRenderable item = args.Scope[i];
            if (item is Drawable d)
            {
                d.InvalidateVisual();
            }
        }
    }
}
