using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using OpenCvSharp;

namespace Beutl.Graphics.Effects;

[Obsolete("Use MosaicEffect instead.")]
public partial class Mosaic : FilterEffect
{
    public Mosaic()
    {
        ScanProperties<Mosaic>();
    }

    [Range(1, float.MaxValue)]
    public IProperty<float> Scale { get; } = Property.CreateAnimatable(1f);

    [Range(1, float.MaxValue)]
    public IProperty<float> ScaleX { get; } = Property.CreateAnimatable(1f);

    [Range(1, float.MaxValue)]
    public IProperty<float> ScaleY { get; } = Property.CreateAnimatable(1f);

    public override void ApplyTo(FilterEffectContext context)
    {
        context.CustomEffect(
            (scaleX: Scale.CurrentValue * ScaleX.CurrentValue, scaleY: Scale.CurrentValue * ScaleY.CurrentValue),
            static (data, effectContext) =>
                effectContext.ForEach((_, target) =>
                {
                    var renderTarget = target.RenderTarget!;
                    var canvas = renderTarget.Value.Canvas;

                    using var src = renderTarget.Snapshot();
                    using var mat = src.ToMat();
                    using var tmp = new Mat((int)(src.Height / data.scaleY), (int)(src.Width / data.scaleX), MatType.CV_8UC4);

                    Cv2.Resize(
                        mat, tmp,
                        new OpenCvSharp.Size(src.Width / data.scaleX, src.Height / data.scaleY),
                        interpolation: InterpolationFlags.Nearest);

                    Cv2.Resize(
                        tmp, mat,
                        new OpenCvSharp.Size(src.Width, src.Height),
                        interpolation: InterpolationFlags.Nearest);

                    canvas.Clear();

                    using var mosaic = mat.ToSKBitmap();
                    canvas.DrawBitmap(mosaic, 0, 0);
                }));
    }
}
