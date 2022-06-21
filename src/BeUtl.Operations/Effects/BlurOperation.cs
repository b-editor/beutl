using BeUtl.Graphics.Effects.OpenCv;
using BeUtl.Media;

namespace BeUtl.Operations.Effects;

public sealed class BlurOperation : BitmapEffectOperation<Blur>
{
    public static readonly CoreProperty<PixelSize> KernelSizeProperty;
    public static readonly CoreProperty<bool> FixImageSizeProperty;

    static BlurOperation()
    {
        KernelSizeProperty = ConfigureProperty<PixelSize, BlurOperation>(nameof(KernelSize))
            .OverrideMetadata(DefaultMetadatas.KernelSize)
            .DefaultValue(new PixelSize(25, 25))
            .Accessor(o => o.KernelSize, (o, v) => o.KernelSize = v)
            .Register();

        FixImageSizeProperty = ConfigureProperty<bool, BlurOperation>(nameof(FixImageSize))
            .OverrideMetadata(DefaultMetadatas.FixImageSize)
            .DefaultValue(false)
            .Accessor(o => o.FixImageSize, (o, v) => o.FixImageSize = v)
            .Register();
    }

    public PixelSize KernelSize
    {
        get => Effect.KernelSize;
        set => Effect.KernelSize = value;
    }

    public bool FixImageSize
    {
        get => Effect.FixImageSize;
        set => Effect.FixImageSize = value;
    }

    public override Blur Effect { get; } = new();
}
