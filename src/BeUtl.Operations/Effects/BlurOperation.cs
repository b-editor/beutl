using BeUtl.Graphics;
using BeUtl.Graphics.Effects;
using BeUtl.Media;

namespace BeUtl.Operations.Effects;

public sealed class BlurOperation : BitmapEffectOperation<Blur>
{
    public static readonly CoreProperty<PixelSize> KernelSizeProperty;

    static BlurOperation()
    {
        KernelSizeProperty = ConfigureProperty<PixelSize, BlurOperation>(nameof(KernelSize))
            .OverrideMetadata(DefaultMetadatas.KernelSize)
            .DefaultValue(new PixelSize(25, 25))
            .Accessor(o => o.KernelSize, (o, v) => o.KernelSize = v)
            .Register();
    }

    public PixelSize KernelSize
    {
        get => Effect.KernelSize;
        set => Effect.KernelSize = value;
    }

    public override Blur Effect { get; } = new();
}
