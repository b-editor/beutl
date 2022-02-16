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
            .OverrideMetadata(new OperationPropertyMetadata<int>
            {
                Header = "S.Common.StartIndex",
                PropertyFlags = PropertyFlags.Designable,
                DefaultValue = 0,
                Minimum = 0,
                SerializeName = "start"
            })
            .Register();

        CountProperty = ConfigureProperty<int, RenderAllOperation>(nameof(Count))
            .Accessor(o => o.Count, (o, v) => o.Count = v)
            .OverrideMetadata(new OperationPropertyMetadata<int>
            {
                Header = "S.Common.Count",
                PropertyFlags = PropertyFlags.Designable,
                DefaultValue = -1,
                Minimum = -1,
                SerializeName = "count"
            })
            .Register();
    }

    public int Start { get; set; }

    public int Count { get; set; } = -1;

    // Todo: LayerOperation Api
    //public override void ApplySetters(in OperationRenderArgs args)
    //{
    //    base.ApplySetters(args);
    //    int start = Math.Max(Start, 0);
    //    int count = Count;
    //    if (count < 0)
    //    {
    //        count = args.Scope.Count;
    //    }

    //    for (int i = start; i < count; i++)
    //    {
    //        IRenderable item = args.Scope[i];
    //        if (item is Drawable d)
    //        {
    //            d.Invalidate();
    //        }
    //    }
    //}
}
