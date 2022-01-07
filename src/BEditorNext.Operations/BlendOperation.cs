using BEditorNext.Graphics;
using BEditorNext.Media;
using BEditorNext.ProjectSystem;

namespace BEditorNext.Operations;

public sealed class BlendOperation : ConfigureOperation<IDrawable>
{
    public static readonly PropertyDefine<float> OpacityProperty;
    public static readonly PropertyDefine<BlendMode> BlendModeProperty;

    static BlendOperation()
    {
        OpacityProperty = RegisterProperty<float, BlendOperation>(nameof(Opacity), (owner, obj) => owner.Opacity = obj, owner => owner.Opacity)
            .Animatable(true)
            .EnableEditor()
            .Maximum(100)
            .Minimum(0)
            .DefaultValue(100)
            .Header("OpacityString")
            .JsonName("opacity");

        BlendModeProperty = RegisterProperty<BlendMode, BlendOperation>(nameof(BlendMode), (o, v) => o.BlendMode = v, o => o.BlendMode)
            .EnableEditor()
            .DefaultValue(BlendMode.SrcOver)
            .Header("BlendString")
            .JsonName("blendMode");
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
