using BeUtl.Graphics;
using BeUtl.Media;
using BeUtl.ProjectSystem;

namespace BeUtl.Operations;

public sealed class AlignOperation : LayerOperation
{
    public static readonly CoreProperty<AlignmentX> CanvasAlignmentXProperty;
    public static readonly CoreProperty<AlignmentY> CanvasAlignmentYProperty;
    public static readonly CoreProperty<AlignmentX> AlignmentXProperty;
    public static readonly CoreProperty<AlignmentY> AlignmentYProperty;

    static AlignOperation()
    {
        CanvasAlignmentXProperty = ConfigureProperty<AlignmentX, AlignOperation>(nameof(CanvasAlignmentX))
            .Accessor(o => o.CanvasAlignmentX, (o, v) => o.CanvasAlignmentX = v)
            .OverrideMetadata(DefaultMetadatas.CanvasAlignmentX)
            .Register();

        CanvasAlignmentYProperty = ConfigureProperty<AlignmentY, AlignOperation>(nameof(CanvasAlignmentY))
            .Accessor(o => o.CanvasAlignmentY, (o, v) => o.CanvasAlignmentY = v)
            .OverrideMetadata(DefaultMetadatas.CanvasAlignmentY)
            .Register();

        AlignmentXProperty = ConfigureProperty<AlignmentX, AlignOperation>(nameof(AlignmentX))
            .Accessor(o => o.AlignmentX, (o, v) => o.AlignmentX = v)
            .OverrideMetadata(DefaultMetadatas.AlignmentX)
            .Register();

        AlignmentYProperty = ConfigureProperty<AlignmentY, AlignOperation>(nameof(AlignmentY))
            .Accessor(o => o.AlignmentY, (o, v) => o.AlignmentY = v)
            .OverrideMetadata(DefaultMetadatas.AlignmentY)
            .Register();
    }

    public AlignmentX CanvasAlignmentX { get; set; }

    public AlignmentY CanvasAlignmentY { get; set; }

    public AlignmentX AlignmentX { get; set; }

    public AlignmentY AlignmentY { get; set; }

    protected override void RenderCore(ref OperationRenderArgs args)
    {
        if (args.Result is Drawable drawable)
        {
            if (!IsEnabled)
            {
                drawable.CanvasAlignmentX = AlignmentX.Left;
                drawable.AlignmentX = AlignmentX.Left;
                drawable.CanvasAlignmentY = AlignmentY.Top;
                drawable.AlignmentY = AlignmentY.Top;
            }
            else
            {
                drawable.CanvasAlignmentX = CanvasAlignmentX;
                drawable.AlignmentX = AlignmentX;
                drawable.CanvasAlignmentY = CanvasAlignmentY;
                drawable.AlignmentY = AlignmentY;
            }
        }
    }
}
