using BEditorNext.Graphics;
using BEditorNext.Media;
using BEditorNext.ProjectSystem;

namespace BEditorNext.Operations;

public sealed class BlendOperation : ConfigureOperation<IDrawable>
{
    public static readonly CoreProperty<float> OpacityProperty;
    public static readonly CoreProperty<BlendMode> BlendModeProperty;

    static BlendOperation()
    {
        OpacityProperty = ConfigureProperty<float, BlendOperation>(nameof(Opacity))
            .Accessor(o => o.Opacity, (o, v) => o.Opacity = v)
            .Animatable(true)
            .EnableEditor()
            .Maximum(100)
            .Minimum(0)
            .DefaultValue(100)
            .Header("OpacityString")
            .JsonName("opacity")
            .Register();

        BlendModeProperty = ConfigureProperty<BlendMode, BlendOperation>(nameof(BlendMode))
            .Accessor(o => o.BlendMode, (o, v) => o.BlendMode = v)
            .EnableEditor()
            .DefaultValue(BlendMode.SrcOver)
            .Header("BlendString")
            .JsonName("blendMode")
            .Register();
    }

    public float Opacity { get; set; }

    public BlendMode BlendMode { get; set; }

    public override void Configure(in OperationRenderArgs args, ref IDrawable obj)
    {
        obj.BlendMode = BlendMode;

        if (obj.Foreground is Brush brush)
        {
            brush.Opacity = Opacity / 100;
        }
    }
}
