using Beutl.Animation;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Utilities;

using SkiaSharp;

using Cv = OpenCvSharp;

namespace Beutl.Graphics.Effects;

public class FlatShadow : FilterEffect
{
    public static readonly CoreProperty<float> AngleProperty;
    public static readonly CoreProperty<float> LengthProperty;
    public static readonly CoreProperty<IBrush?> BrushProperty;
    public static readonly CoreProperty<bool> ShadowOnlyProperty;
    private float _angle;
    private float _length;
    private IBrush? _brush;
    private bool _shadowOnly;

    static FlatShadow()
    {
        AngleProperty = ConfigureProperty<float, FlatShadow>(nameof(Angle))
            .Accessor(o => o.Angle, (o, v) => o.Angle = v)
            .Register();

        LengthProperty = ConfigureProperty<float, FlatShadow>(nameof(Length))
            .Accessor(o => o.Length, (o, v) => o.Length = v)
            .Register();

        BrushProperty = ConfigureProperty<IBrush?, FlatShadow>(nameof(Brush))
            .Accessor(o => o.Brush, (o, v) => o.Brush = v)
            .Register();

        ShadowOnlyProperty = ConfigureProperty<bool, FlatShadow>(nameof(ShadowOnly))
            .Accessor(o => o.ShadowOnly, (o, v) => o.ShadowOnly = v)
            .Register();

        AffectsRender<FlatShadow>(AngleProperty, LengthProperty, BrushProperty, ShadowOnlyProperty);
    }

    public FlatShadow()
    {
        Brush = new SolidColorBrush(Colors.Gray);
    }

    public float Angle
    {
        get => _angle;
        set => SetAndRaise(AngleProperty, ref _angle, value);
    }

    public float Length
    {
        get => _length;
        set => SetAndRaise(LengthProperty, ref _length, value);
    }

    public IBrush? Brush
    {
        get => _brush;
        set => SetAndRaise(BrushProperty, ref _brush, value);
    }

    public bool ShadowOnly
    {
        get => _shadowOnly;
        set => SetAndRaise(ShadowOnlyProperty, ref _shadowOnly, value);
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        (Brush as IAnimatable)?.ApplyAnimations(clock);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.Custom((Angle, Length, Brush, ShadowOnly), Apply, TransformBounds);
    }

    public override Rect TransformBounds(Rect bounds)
    {
        return TransformBounds((Angle, Length, Brush, ShadowOnly), bounds);
    }

    private static Rect TransformBounds((float Angle, float Length, IBrush? Brush, bool ShadowOnly) data, Rect rect)
    {
        float length = data.Length;
        float radian = MathUtilities.Deg2Rad(data.Angle);
        float x = length * MathF.Cos(radian);
        float y = length * MathF.Sin(radian);
        float xAbs = Math.Abs(x);
        float yAbs = Math.Abs(y);

        float width = rect.Width + xAbs;
        float height = rect.Height + yAbs;

        return new Rect(rect.X - (xAbs - x) / 2, rect.Y - (yAbs - y) / 2, width, height);
    }

    private static void Apply((float Angle, float Length, IBrush? Brush, bool ShadowOnly) data, FilterEffectCustomOperationContext context)
    {
        static Cv.Point[][] FindPoints(Bitmap<Bgra8888> src)
        {
            using Cv.Mat srcMat = src.ToMat();
            using Cv.Mat alphaMat = srcMat.ExtractChannel(3);

            // 輪郭検出
            alphaMat.FindContours(
                out Cv.Point[][] points,
                out Cv.HierarchyIndex[] h,
                Cv.RetrievalModes.List,
                Cv.ContourApproximationModes.ApproxSimple);

            return points;
        }
        static SKPath CreatePath(Cv.Point[][] points)
        {
            var skpath = new SKPath();
            foreach (Cv.Point[] inner in points)
            {
                bool first = true;
                foreach (Cv.Point item in inner)
                {
                    if (first)
                    {
                        skpath.MoveTo(item.X, item.Y);
                        first = false;
                    }
                    else
                    {
                        skpath.LineTo(item.X, item.Y);
                    }
                }

                skpath.Close();
            }

            return skpath;
        }

        IBrush? brush = data.Brush;
        float length = data.Length;
        float radian = MathUtilities.Deg2Rad(data.Angle);

        if (context.Target.Surface is { } srcSurface)
        {
            using SKImage skImage = srcSurface.Value.Snapshot();
            using var srcBitmap = skImage.ToBitmap();
            Cv.Point[][] points = FindPoints(srcBitmap);

            float x1 = MathF.Cos(radian);
            float y1 = MathF.Sin(radian);
            float x2 = length * x1;
            float y2 = length * y1;
            float x2Abs = Math.Abs(x2);
            float y2Abs = Math.Abs(y2);

            Size size = context.Target.Size;
            using EffectTarget newTarget = context.CreateTarget((int)(size.Width + x2Abs), (int)(size.Height + y2Abs));
            using ImmediateCanvas newCanvas = context.Open(newTarget);

            using var paint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };

            var c = new BrushConstructor(newTarget.Size, brush, BlendMode.SrcIn, newCanvas);
            using var brushPaint = new SKPaint();
            c.ConfigurePaint(brushPaint);

            using SKPath path = CreatePath(points);

            using (newCanvas.PushLayer())
            using (newCanvas.PushTransform(Matrix.CreateTranslation((x2Abs - x2) / 2, (y2Abs - y2) / 2)))
            {
                float lenAbs = Math.Abs(length);
                int unit = Math.Sign(length);
                for (int i = 0; i < lenAbs; i++)
                {
                    newCanvas.Transform = Matrix.CreateTranslation(x1 * unit, y1 * unit) * newCanvas.Transform;
                    newCanvas.Canvas.DrawPath(path, paint);
                }
            }

            newCanvas.Canvas.DrawRect(SKRect.Create(newTarget.Size.ToSKSize()), brushPaint);

            if (!data.ShadowOnly)
                newCanvas.DrawSurface(srcSurface.Value, new((x2Abs - x2) / 2, (y2Abs - y2) / 2));

            context.ReplaceTarget(newTarget);
        }
    }
}
