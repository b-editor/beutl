using BeUtl.Graphics;
using BeUtl.Media;
using BeUtl.ProjectSystem;

namespace BeUtl.Operations.Configure;

public sealed class BlendOperation : LayerOperation
{
    public static readonly CoreProperty<float> OpacityProperty;
    public static readonly CoreProperty<BlendMode> BlendModeProperty;

    static BlendOperation()
    {
        OpacityProperty = ConfigureProperty<float, BlendOperation>(nameof(Opacity))
            .Accessor(o => o.Opacity, (o, v) => o.Opacity = v)
            .OverrideMetadata(DefaultMetadatas.Opacity)
            .Register();

        BlendModeProperty = ConfigureProperty<BlendMode, BlendOperation>(nameof(BlendMode))
            .Accessor(o => o.BlendMode, (o, v) => o.BlendMode = v)
            .OverrideMetadata(DefaultMetadatas.BlendMode)
            .Register();
    }

    public float Opacity { get; set; }

    public BlendMode BlendMode { get; set; }

    protected override void RenderCore(ref OperationRenderArgs args)
    {
        if (args.Result is not Graphics.Drawable obj) return;
        obj.BlendMode = BlendMode;

        if (obj.Foreground is Brush brush)
        {
            brush.Opacity = Opacity / 100;
        }
    }
}
