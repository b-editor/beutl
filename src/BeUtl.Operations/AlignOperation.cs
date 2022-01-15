using BeUtl.Graphics;
using BeUtl.Media;
using BeUtl.ProjectSystem;

namespace BeUtl.Operations;

public sealed class AlignOperation : ConfigureOperation<IDrawable>
{
    public static readonly CoreProperty<AlignmentX> HorizontalAlignmentProperty;
    public static readonly CoreProperty<AlignmentY> VerticalAlignmentProperty;
    public static readonly CoreProperty<AlignmentX> HorizontalContentAlignmentProperty;
    public static readonly CoreProperty<AlignmentY> VerticalContentAlignmentProperty;

    static AlignOperation()
    {
        HorizontalAlignmentProperty = ConfigureProperty<AlignmentX, AlignOperation>(nameof(HorizontalAlignment))
            .Accessor(o => o.HorizontalAlignment, (o, v) => o.HorizontalAlignment = v)
            .EnableEditor()
            .Header("HorizontalAlignmentString")
            .JsonName("hAlilgn")
            .Register();

        VerticalAlignmentProperty = ConfigureProperty<AlignmentY, AlignOperation>(nameof(VerticalAlignment))
            .Accessor(o => o.VerticalAlignment, (o, v) => o.VerticalAlignment = v)
            .EnableEditor()
            .Header("VerticalAlignmentString")
            .JsonName("vAlign")
            .Register();

        HorizontalContentAlignmentProperty = ConfigureProperty<AlignmentX, AlignOperation>(nameof(HorizontalContentAlignment))
            .Accessor(o => o.HorizontalContentAlignment, (o, v) => o.HorizontalContentAlignment = v)
            .EnableEditor()
            .Header("HorizontalContentAlignmentString")
            .JsonName("hContentAlign")
            .Register();

        VerticalContentAlignmentProperty = ConfigureProperty<AlignmentY, AlignOperation>(nameof(VerticalContentAlignment))
            .Accessor(o => o.VerticalContentAlignment, (o, v) => o.VerticalContentAlignment = v)
            .EnableEditor()
            .Header("VerticalContentAlignmentString")
            .JsonName("vContentAlign")
            .Register();
    }

    public AlignmentX HorizontalAlignment { get; set; }

    public AlignmentY VerticalAlignment { get; set; }

    public AlignmentX HorizontalContentAlignment { get; set; }

    public AlignmentY VerticalContentAlignment { get; set; }

    public override void Configure(in OperationRenderArgs args, ref IDrawable obj)
    {
        obj.HorizontalAlignment = HorizontalAlignment;
        obj.HorizontalContentAlignment = HorizontalContentAlignment;
        obj.VerticalAlignment = VerticalAlignment;
        obj.VerticalContentAlignment = VerticalContentAlignment;
    }
}
