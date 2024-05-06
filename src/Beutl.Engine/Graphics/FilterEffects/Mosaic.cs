using System.ComponentModel.DataAnnotations;
using OpenCvSharp;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public class Mosaic : FilterEffect
{
    public static readonly CoreProperty<float> ScaleProperty;
    public static readonly CoreProperty<float> ScaleXProperty;
    public static readonly CoreProperty<float> ScaleYProperty;
    private float _scale = 1;
    private float _scaleX = 1;
    private float _scaleY = 1;

    static Mosaic()
    {
        ScaleProperty = ConfigureProperty<float, Mosaic>(nameof(Scale))
            .Accessor(o => o.Scale, (o, v) => o.Scale = v)
            .DefaultValue(1)
            .Register();

        ScaleXProperty = ConfigureProperty<float, Mosaic>(nameof(ScaleX))
            .Accessor(o => o.ScaleX, (o, v) => o.ScaleX = v)
            .DefaultValue(1)
            .Register();

        ScaleYProperty = ConfigureProperty<float, Mosaic>(nameof(ScaleY))
            .Accessor(o => o.ScaleY, (o, v) => o.ScaleY = v)
            .DefaultValue(1)
            .Register();

        AffectsRender<Mosaic>(ScaleProperty, ScaleXProperty, ScaleYProperty);
    }

    [Range(1, float.MaxValue)]
    public float Scale
    {
        get => _scale;
        set => SetAndRaise(ScaleProperty, ref _scale, value);
    }

    [Range(1, float.MaxValue)]
    public float ScaleX
    {
        get => _scaleX;
        set => SetAndRaise(ScaleXProperty, ref _scaleX, value);
    }

    [Range(1, float.MaxValue)]
    public float ScaleY
    {
        get => _scaleY;
        set => SetAndRaise(ScaleYProperty, ref _scaleY, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.CustomEffect((Scale * ScaleX, Scale * ScaleY), (d, c) =>
            c.ForEach((_, t) =>
            {
                var surface = t.Surface!;
                var canvas = surface.Value.Canvas;

                using SKImage skImage = surface.Value.Snapshot();
                using var src = skImage.ToBitmap();
                using var mat = src.ToMat();
                using var tmp = new Mat((int)(src.Height / d.Item2), (int)(src.Width / d.Item1), MatType.CV_8UC4);

                Cv2.Resize(
                    mat, tmp,
                    new OpenCvSharp.Size(src.Width / d.Item1, src.Height / d.Item2),
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
