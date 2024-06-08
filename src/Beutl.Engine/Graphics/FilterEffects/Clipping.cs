using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Pixel;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed class Clipping : FilterEffect
{
    [Obsolete("Use separate properties for each side of the thickness.")]
    public static readonly CoreProperty<Thickness> ThicknessProperty;

    public static readonly CoreProperty<float> LeftProperty;
    public static readonly CoreProperty<float> TopProperty;
    public static readonly CoreProperty<float> RightProperty;
    public static readonly CoreProperty<float> BottomProperty;
    public static readonly CoreProperty<bool> AutoCenterProperty;
    public static readonly CoreProperty<bool> AutoClipProperty;
    private float _left;
    private float _top;
    private float _right;
    private float _bottom;
    private bool _autoCenter;
    private bool _autoClip;

    static Clipping()
    {
#pragma warning disable CS0618
        ThicknessProperty = ConfigureProperty<Thickness, Clipping>(nameof(Thickness))
            .Accessor(o => o.Thickness, (o, v) => o.Thickness = v)
            .Register();

        LeftProperty = ConfigureProperty<float, Clipping>(nameof(Left))
            .Accessor(o => o.Thickness.Left, (o, v) => o.Thickness = o.Thickness.WithLeft(v))
            .Register();

        TopProperty = ConfigureProperty<float, Clipping>(nameof(Top))
            .Accessor(o => o.Thickness.Top, (o, v) => o.Thickness = o.Thickness.WithTop(v))
            .Register();

        RightProperty = ConfigureProperty<float, Clipping>(nameof(Right))
            .Accessor(o => o.Thickness.Right, (o, v) => o.Thickness = o.Thickness.WithRight(v))
            .Register();

        BottomProperty = ConfigureProperty<float, Clipping>(nameof(Bottom))
            .Accessor(o => o.Thickness.Bottom, (o, v) => o.Thickness = o.Thickness.WithBottom(v))
            .Register();

        AutoCenterProperty = ConfigureProperty<bool, Clipping>(nameof(AutoCenter))
            .Accessor(o => o.AutoCenter, (o, v) => o.AutoCenter = v)
            .DefaultValue(false)
            .Register();

        AutoClipProperty = ConfigureProperty<bool, Clipping>(nameof(AutoClip))
            .Accessor(o => o.AutoClip, (o, v) => o.AutoClip = v)
            .DefaultValue(true)
            .Register();

        AffectsRender<Clipping>(
            ThicknessProperty,
            LeftProperty,
            TopProperty,
            RightProperty,
            BottomProperty,
            AutoCenterProperty,
            AutoClipProperty);
#pragma warning restore CS0618
    }

    [Obsolete("Use separate properties for each side of the thickness.")]
    [Display(Name = nameof(Strings.Thickness), ResourceType = typeof(Strings))]
    [NotAutoSerialized]
    [Browsable(false)]
    public Thickness Thickness
    {
        get => new(_left, _top, _right, _bottom);
        set
        {
            var thickness = Thickness;
            SetAndRaise(ThicknessProperty, ref thickness, value);
            (Left, Top, Right, Bottom) = (thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);
        }
    }

    [Display(Name = nameof(Strings.Left), ResourceType = typeof(Strings))]
    public float Left
    {
        get => _left;
        set => SetAndRaise(LeftProperty, ref _left, value);
    }

    [Display(Name = nameof(Strings.Top), ResourceType = typeof(Strings))]
    public float Top
    {
        get => _top;
        set => SetAndRaise(TopProperty, ref _top, value);
    }

    [Display(Name = nameof(Strings.Right), ResourceType = typeof(Strings))]
    public float Right
    {
        get => _right;
        set => SetAndRaise(RightProperty, ref _right, value);
    }

    [Display(Name = nameof(Strings.Bottom), ResourceType = typeof(Strings))]
    public float Bottom
    {
        get => _bottom;
        set => SetAndRaise(BottomProperty, ref _bottom, value);
    }

    [Display(Name = nameof(Strings.AutomaticCentering), ResourceType = typeof(Strings))]
    public bool AutoCenter
    {
        get => _autoCenter;
        set => SetAndRaise(AutoCenterProperty, ref _autoCenter, value);
    }

    [Display(Name = nameof(Strings.ClipTransparentArea), ResourceType = typeof(Strings))]
    public bool AutoClip
    {
        get => _autoClip;
        set => SetAndRaise(AutoClipProperty, ref _autoClip, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.CustomEffect((new Thickness(Left, Top, Right, Bottom), AutoCenter, AutoClip), Apply, TransformBounds);
    }

    public override Rect TransformBounds(Rect bounds)
    {
        return TransformBounds((new Thickness(Left, Top, Right, Bottom), AutoCenter, AutoClip), bounds);
    }

    private Rect TransformBounds((Thickness thickness, bool autoCenter, bool autoClip) data, Rect rect)
    {
        if (data.autoClip) return Rect.Invalid;

        var result = rect.Deflate(data.thickness).Normalize();
        if (data.autoCenter)
        {
            result = rect.CenterRect(result);
        }

        return result;
    }

    private static Thickness FindRectAndReturnThickness(SKSurface surface)
    {
        using var image = surface.Snapshot();
        // 透明度のみ
        using var bitmap = new Bitmap<Grayscale8>(image.Width, image.Height);
        image.ReadPixels(
            new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Alpha8),
            bitmap.Data,
            bitmap.Width,
            0, 0);

        int x0 = bitmap.Width;
        int y0 = bitmap.Height;
        int x1 = 0;
        int y1 = 0;

        // 透明でないピクセルを探す
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap[x, y].Value != 0)
                {
                    if (x0 > x) x0 = x;
                    if (y0 > y) y0 = y;
                    if (x1 < x) x1 = x;
                    if (y1 < y) y1 = y;
                }
            }
        }

        return new Thickness(x0, y0, bitmap.Width - x1, bitmap.Height - y1);
    }

    private static void Apply((Thickness thickness, bool autoCenter, bool autoClip) data, CustomFilterEffectContext context)
    {
        Thickness originalThickness = data.thickness;
        bool autoCenter = data.autoCenter;
        for (int i = 0; i < context.Targets.Count; i++)
        {
            Thickness thickness = originalThickness;
            var target = context.Targets[i];
            var surface = target.Surface!.Value;
            if (data.autoClip)
            {
                thickness += FindRectAndReturnThickness(surface);
            }


            Rect originalRect = target.Bounds.WithX(0).WithY(0);
            // 結果的なrect (originalRect内)
            Rect clipRect = originalRect.Deflate(thickness).Normalize();
            // 特にクリッピングの部分、領域拡張ではなく
            Rect intersect = originalRect.Intersect(clipRect);

            if (intersect.IsEmpty || clipRect.Width == 0 || clipRect.Height == 0)
            {
                context.Targets.RemoveAt(i);
                i--;
            }
            else
            {
                // クリッピング機能と領域展開機能をつけたらコードが汚くなりました。
                // 誰かリファクタリングしてください。移項するだけで十分です。
                // pointX = 1 - leftの小数点部分
                float pointX = MathF.CopySign(MathF.Ceiling(thickness.Left) - thickness.Left, thickness.Left);
                float pointY = MathF.CopySign(MathF.Ceiling(thickness.Top) - thickness.Top, thickness.Top);

                using SKImage skImage = surface.Snapshot(SKRectI.Floor(intersect.ToSKRect()));

                // 新しいBounds
                var newBounds = clipRect
                    .WithX(target.Bounds.X + thickness.Left - pointX)
                    .WithY(target.Bounds.Y + thickness.Top - pointY);
                if (thickness.Left > 0)
                {
                    newBounds = newBounds.WithX(target.Bounds.X + thickness.Left + pointX - 1);
                    pointX = 0;
                }

                if (thickness.Top > 0)
                {
                    newBounds = newBounds.WithY(target.Bounds.Y + thickness.Top + pointY - 1);
                    pointY = 0;
                }

                EffectTarget newTarget;
                // センタリング
                if (autoCenter)
                {
                    newTarget = context.CreateTarget(target.Bounds.CenterRect(newBounds));
                }
                else
                {
                    newTarget = context.CreateTarget(newBounds);
                }

                newTarget.Surface?.Value?.Canvas?.DrawImage(
                    skImage,
                    intersect.X - clipRect.X + pointX,
                    intersect.Y - clipRect.Y + pointY);
                // Debug.WriteLine(target.Bounds.X + thickness.Left - pointX);

                target.Dispose();
                context.Targets[i] = newTarget;
            }
        }
    }
}
